using Xunit;
using Xunit.Abstractions;
using Amazon.Lambda.TestUtilities;
using System.Web;
using Moq;
using Amazon.DynamoDBv2.DataModel;
using OtpNet;

namespace TOTPFunction.Tests;

public class FunctioUnitTest
{

    private readonly ITestOutputHelper _output;  

    public FunctioUnitTest(ITestOutputHelper output) 
    {
        _output = output;
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
        var mockDbContext = new Mock<IDynamoDBContext>();

        // Setup the SaveAsync mock behaviour
        mockDbContext
            .Setup(context => context.SaveAsync(
                It.IsAny<TOTPEntity>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var function = new Function(mockDbContext.Object);
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

        var mockDbContext = new Mock<IDynamoDBContext>();

        var mockEntity = new TOTPEntity 
        {
                AppName = "test",
                UserId = "userId",
                Secret = totpSecret
        };
        // Setup the SaveAsync mock behaviour
        mockDbContext
            .Setup(context => context.LoadAsync<TOTPEntity>(
                It.IsAny<string>(), 
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockEntity);

        var function = new Function(mockDbContext.Object);
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
        var mockDbContext = new Mock<IDynamoDBContext>();

        mockDbContext
            .Setup(context => context.DeleteAsync(
                It.IsAny<TOTPEntity>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var function = new Function(mockDbContext.Object);
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
