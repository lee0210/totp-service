using Amazon.Lambda.Core;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using System.Text.Json;
using System.Security.Cryptography;
using OtpNet;

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
    public string? OTP { get; set; }

}

public class FunctionOutput
{
    public required StatusCodes StatusCode { get; set; }
    public string? TOTPString { get; set; }
    public bool? IsValid { get; set; }
}

public class Function
{
    private readonly IDynamoDBContext _dbContext;
    private const int SECRET_LENGTH = 32;

    public Function()
    {
        var tablePrefix = Environment.GetEnvironmentVariable("TABLE_PREFIX") ?? throw new Exception("TABLE_PREFIX environment variable is not set");
        _dbContext = new DynamoDBContext(
            new AmazonDynamoDBClient(), 
            new DynamoDBContextConfig { TableNamePrefix = tablePrefix }
        );
    }

    public Function(IDynamoDBContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<FunctionOutput> FunctionHandler(FunctionInput input, ILambdaContext context)
    {
        if (!isValidInput(input))
        {
            return new FunctionOutput { StatusCode = StatusCodes.BadRequest };
        }

        switch (input.Action)
        {
            case "create":
                return await create(input);
            case "verify":
                return await verify(input);
            case "delete":
                return await delete(input);
            default:
                return new FunctionOutput { StatusCode = StatusCodes.BadRequest };
        }
    }

    private bool isValidInput(FunctionInput input)
    {
        if (input.Action == null || input.AppName == null || input.UserId == null)
        {
            return false;
        }
        if (input.Action == "verify" && input.OTP == null)
        {
            return false;
        }
        return true;
    }

    private async Task<FunctionOutput> create(FunctionInput input)
    {
        try
        {
            var totp = new TOTPEntity
            {
                AppName = input.AppName,
                UserId = input.UserId,
                Secret = GenerateTotpSecret(SECRET_LENGTH),
                CreatedAt = DateTime.Now
            };

            await _dbContext.SaveAsync(totp);

            return new FunctionOutput
            {
                StatusCode = StatusCodes.OK,
                TOTPString = $"otpauth://totp/{totp.AppName}:{totp.UserId}?secret={totp.Secret}&issuer={totp.AppName}"
            };
        }
        catch (Exception e)
        {
            LambdaLogger.Log(e.ToString());
            return new FunctionOutput { StatusCode = StatusCodes.InternalServerError };
        }
    }

    private async Task<FunctionOutput> verify(FunctionInput input)
    {
        try {
            TOTPEntity totpE = await _dbContext.LoadAsync<TOTPEntity>(TOTPEntity.getPK(input.AppName, input.UserId));
            if (totpE == null)
            {
                return new FunctionOutput { StatusCode = StatusCodes.BadRequest };
            }
            var totp = new Totp(Base32Encoding.ToBytes(totpE.Secret));
            if (totp.VerifyTotp(input.OTP, out _))
            {
                return new FunctionOutput
                {
                    StatusCode = StatusCodes.OK,
                    IsValid = true
                };
            }
            return new FunctionOutput
            {
                StatusCode = StatusCodes.OK,
                IsValid = false
            };
        } catch (Exception e)
        {
            LambdaLogger.Log(e.ToString());
            return new FunctionOutput { StatusCode = StatusCodes.InternalServerError };
        }
    }

    private async Task<FunctionOutput> delete(FunctionInput input)
    {
        try
        {
            await _dbContext.DeleteAsync<TOTPEntity>(TOTPEntity.getPK(input.AppName, input.UserId));
            return new FunctionOutput { StatusCode = StatusCodes.OK };   
        }
        catch (Exception e)
        {
            LambdaLogger.Log(e.ToString());
            return new FunctionOutput { StatusCode = StatusCodes.InternalServerError };
        }
        
    }

    private static string GenerateTotpSecret(int length)
    {
        var rng = RandomNumberGenerator.Create();
        var keyBytes = new byte[length];
        rng.GetBytes(keyBytes);

        return Base32Encoding.ToString(keyBytes);
    }
}
