using Amazon.Lambda.Core;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using System.Security.Cryptography;
using OtpNet;
using TOTPFunction.Model;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace TOTPFunction;

public enum StatusCodes
{
    OK = 200,
    BadRequest = 400,
    InternalServerError = 500
}

public class FunctionInput
{
    // Action to be performed. Avaliable values: "create", "verify" and "delete"
    public required string Action { get; set; }
    // Name of the application requesting TOTP
    public required string AppName { get; set; }
    // Unique identifier for the user
    public required string UserId { get; set; }
    // One-time password value for validation
    public string? Device { get; set; }
    public string? OTP { get; set; }

}

public class FunctionOutput
{
    public required StatusCodes StatusCode { get; set; }
}

public class CreateResult : FunctionOutput
{
    public required string TOTPString { get; set; }

}

public class VerifyResult : FunctionOutput
{
    public required bool IsValid { get; set; }
}

public class ListResult : FunctionOutput
{
    public required List<TotpDTO> Totps { get; set; }
}

public class TotpDTO
{
    public required string AppName { get; set; }
    public required string UserId { get; set; }
    public required string Device { get; set; }
}

public class Function
{
    private readonly IDynamoDBContext _dbContext;

    private readonly string _issuer = Environment.GetEnvironmentVariable("ISSUER") ?? "Totp Service";
    private const int SECRET_LENGTH = 32;

    public Function()
    {
        var tablePrefix = Environment.GetEnvironmentVariable("TABLE_PREFIX") ?? "";
        _dbContext = new DynamoDBContext(
            new AmazonDynamoDBClient(),
            new DynamoDBContextConfig { TableNamePrefix = tablePrefix }
        );
    }

    public Function(IDynamoDBContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<dynamic> FunctionHandler(FunctionInput input, ILambdaContext context)
    {
        if (!IsValidInput(input))
        {
            return new FunctionOutput { StatusCode = StatusCodes.BadRequest };
        }

        try
        {
            return input.Action switch
            {
                "create" => await Create(input),
                "verify" => await Verify(input),
                "delete" => await Delete(input),
                "list" => await List(input),
                _ => new FunctionOutput { StatusCode = StatusCodes.BadRequest },
            };
        }
        catch (Exception e)
        {
            context.Logger.Log(e.Message);
            return new FunctionOutput { StatusCode = StatusCodes.InternalServerError };
        }
    }

    private bool IsValidInput(FunctionInput input)
    {
        return input.Action switch
        {
            "create" => !String.IsNullOrEmpty(input.Device),
            "delete" => !String.IsNullOrEmpty(input.Device),
            "verify" => !String.IsNullOrEmpty(input.OTP),
            _ => true
        };
    }

    private async Task<FunctionOutput> Create(FunctionInput input)
    {
        var totp = new TOTPEntity
        {
            AppName = input.AppName,
            UserId = input.UserId,
            Device = input.Device!,
            Secret = GenerateTotpSecret(SECRET_LENGTH),
            CreatedAt = DateTime.Now
        };

        await _dbContext.SaveAsync(totp);

        return new CreateResult
        {
            StatusCode = StatusCodes.OK,
            TOTPString = $"otpauth://totp/{totp.AppName}:{totp.Device}?secret={totp.Secret}&issuer={_issuer}"
        };
    }

    private async Task<FunctionOutput> Verify(FunctionInput input)
    {
        var queryResponse = _dbContext.QueryAsync<TOTPEntity>(TOTPEntity.GetPK(input.AppName, input.UserId));
        foreach (var totpEntity in await queryResponse.GetRemainingAsync())
        {
            var totp = new Totp(Base32Encoding.ToBytes(totpEntity.Secret));
            if (totp.VerifyTotp(input.OTP, out _, VerificationWindow.RfcSpecifiedNetworkDelay))
            {
                return new VerifyResult
                {
                    StatusCode = StatusCodes.OK,
                    IsValid = true
                };
            }
        }
        return new VerifyResult
        {
            StatusCode = StatusCodes.OK,
            IsValid = false
        };
    }

    private async Task<FunctionOutput> Delete(FunctionInput input)
    {
        await _dbContext.DeleteAsync<TOTPEntity>(TOTPEntity.GetPK(input.AppName, input.UserId), input.Device);
        return new FunctionOutput { StatusCode = StatusCodes.OK };
    }

    private async Task<ListResult> List(FunctionInput input)
    {
        var queryResponse = _dbContext.QueryAsync<TOTPEntity>(TOTPEntity.GetPK(input.AppName, input.UserId));
        var totps = (await queryResponse.GetRemainingAsync()).Select(
            totpEntity => new TotpDTO
            {
                AppName = totpEntity.AppName,
                UserId = totpEntity.UserId,
                Device = totpEntity.Device
            }).ToList();
        return new ListResult
        {
            StatusCode = StatusCodes.OK,
            Totps = totps
        };
    }

    private static string GenerateTotpSecret(int length)
    {
        var rng = RandomNumberGenerator.Create();
        var keyBytes = new byte[length];
        rng.GetBytes(keyBytes);

        return Base32Encoding.ToString(keyBytes);
    }
}
