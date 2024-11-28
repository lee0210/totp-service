using Amazon.DynamoDBv2.DataModel;

namespace TOTPFunction;

[DynamoDBTable("Totps")]
public class TOTPEntity
{
    [DynamoDBHashKey]
    public string Id {
        get => $"{AppName}-{UserId}";
        set {
            var parts = value.Split('-');
            AppName = parts[0];
            UserId = parts[1];
        }
    }

    [DynamoDBIgnore]
    public required string UserId { get; set; }

    [DynamoDBIgnore]    
    public required string AppName { get; set; }

    public required string Secret { get; set; }

    public DateTime? CreatedAt { get; set; }

    public static string getPK(string appName, string userId) {
        return $"{appName}-{userId}";
    }
}
