using Xunit;
using Xunit.Abstractions;
using Amazon.Lambda.TestUtilities;
using System.Web;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.Model;
using OtpNet;

namespace TOTPFunction.Tests;

public class FunctioIntegrationTest
{

    private readonly ITestOutputHelper _output;  
    private readonly IDynamoDBContext _localDynamoDBContext;

    public FunctioIntegrationTest(ITestOutputHelper output)  
    {
        _output = output;

        var amazonDynamoDBClient = new AmazonDynamoDBClient(
            new Amazon.Runtime.BasicAWSCredentials("accesskey", "secretkey"),
            new AmazonDynamoDBConfig { ServiceURL = "http://dynamodb-local:8000" }
        );

        InitializeDynamoDB(amazonDynamoDBClient).Wait();

        _localDynamoDBContext = new DynamoDBContext(amazonDynamoDBClient);
    }

    private async Task InitializeDynamoDB(IAmazonDynamoDB amazonDynamoDB)
    {
        var tableName = "Totps";
        var request = new CreateTableRequest
        {
            TableName = tableName,
            KeySchema = new List<KeySchemaElement>
            {
                new KeySchemaElement
                {
                    AttributeName = "Id", 
                    KeyType = "HASH"     
                }
            },
            AttributeDefinitions = new List<AttributeDefinition>
            {
                new AttributeDefinition
                {
                    AttributeName = "Id",
                    AttributeType = "S"
                }
            },
            BillingMode = BillingMode.PAY_PER_REQUEST
        };
        try
        {
            await amazonDynamoDB.DeleteTableAsync(tableName);
        } catch (ResourceNotFoundException) {
            _output.WriteLine($"Table ${tableName} not found. Skipping deletion");
        }
        await amazonDynamoDB.CreateTableAsync(request);   
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
        CreateResult result = await function.FunctionHandler(input, context);
        
        _output.WriteLine($"Result: {result.TOTPString}");

        var uri = new Uri(result.TOTPString.Replace("otpauth://", "http://")); 
        var query = HttpUtility.ParseQueryString(uri.Query);
        
        Assert.Equal(StatusCodes.OK, result.StatusCode);

        return query["secret"] ?? "";
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
        VerifyResult result = await function.FunctionHandler(input, context);
        
        Assert.Equal(StatusCodes.OK, result.StatusCode);
        Assert.True(result.IsValid);

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
