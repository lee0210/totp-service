using Xunit;
using Xunit.Abstractions;
using Amazon.Lambda.TestUtilities;
using System.Web;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using OtpNet;

namespace TOTPFunction.Tests;

public class FunctioIntegrationTest
{

    private readonly ITestOutputHelper _output;  
    private readonly IDynamoDBContext _localDynamoDBContext;

    public FunctioIntegrationTest(ITestOutputHelper output)  
    {
        _output = output;

        _localDynamoDBContext = new DynamoDBContext(
            new AmazonDynamoDBClient(
                new Amazon.Runtime.BasicAWSCredentials("accesskey", "secretkey"),
                new AmazonDynamoDBConfig { ServiceURL = "http://dynamodb-local:8000" })
        );
    }

    [Fact]
    public async Task TestHappyFlow()
    {
        var totpSecret = await TestHappyFlow_Create();
        await TestHappyFlow_Verify(totpSecret);
        await TestHappyFlow_Delete();
        
    }

    private async Task<string> TestHappyFlow_Create() 
    {

        var function = new Function(_localDynamoDBContext);
        var context = new TestLambdaContext();
        var input = new FunctionInput
        {
            Action = "create",
            AppName = "test",
            UserId = "userId"
        };
        FunctionOutput result = await function.FunctionHandler(input, context);
        
        _output.WriteLine($"Result: {result.TOTPString}");

        var uri = new Uri(result.TOTPString.Replace("otpauth://", "http://")); 
        var query = HttpUtility.ParseQueryString(uri.Query);
        
        Assert.Equal(StatusCodes.OK, result.StatusCode);

        return query["secret"];
    }

    private async Task TestHappyFlow_Verify(string totpSecret)
    {
        var totp = new Totp(Base32Encoding.ToBytes(totpSecret));
        var otp = totp.ComputeTotp();

        var function = new Function(_localDynamoDBContext);
        var context = new TestLambdaContext();
        var input = new FunctionInput
        {
            Action = "verify",
            AppName = "test",
            UserId = "userId",
            OTP = otp
        };
        FunctionOutput result = await function.FunctionHandler(input, context);
        
        Assert.Equal(StatusCodes.OK, result.StatusCode);
        Assert.Equal(true, result.IsValid);

    }

    private async Task TestHappyFlow_Delete()
    {
        var function = new Function(_localDynamoDBContext);
        var context = new TestLambdaContext();
        var input = new FunctionInput
        {
            Action = "delete",
            AppName = "test",
            UserId = "userId"
        };
        FunctionOutput result = await function.FunctionHandler(input, context);

        Assert.Equal(StatusCodes.OK, result.StatusCode);
    }

}
