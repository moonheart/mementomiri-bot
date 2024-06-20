namespace MementoMori.BotServer.Models;

public class UserGachaInfo
{
    public long Id { get; set; }
    public int PickUpCount { get; set; }
    public int DestinyCount { get; set; }
    public int PlatinumCount { get; set; }
}

public enum GachaType
{
    PickUp,
    Destiny,
    Platinum
}