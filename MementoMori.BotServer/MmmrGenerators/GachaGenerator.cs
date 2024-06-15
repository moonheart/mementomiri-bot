using AutoCtor;
using MementoMori.BotServer.Utils;
using MementoMori.Ortega.Share.Data.Gacha;
using MementoMori.Ortega.Share.Master.Table;

namespace MementoMori.BotServer.MmmrGenerators;
using static MementoMori.Ortega.Share.Masters;

[InjectSingleton]
[AutoConstruct]
public partial class GachaGenerator: GeneratorBase<GachaGenerator>
{
    private readonly ImageUtil _imageUtil;
    
    public async Task<byte[]> Generate(List<GachaItemRate> items)
    {
        // 根据 LotteryRate 随机抽取 10 个物品
        // 其中, items 所有元素的 LotteryRate 和为1 
        
        var result = new List<GachaResultItem>();
        var random = new Random();
        for (var i = 0; i < 10; i++)
        {
            var rate = random.NextDouble();
            var sum = 0.0d;
            foreach (var item in items)
            {
                sum += item.LotteryRate;
                if (rate <= sum)
                {
                    var resultItem = new GachaResultItem
                    {
                        ItemCount = item.Item.ItemCount,
                        ItemId = item.Item.ItemId,
                        ItemType = item.Item.ItemType,
                        CharacterRarityFlags = item.CharacterRarityFlags
                    };
                    result.Add(resultItem);
                    break;
                }
            }
        }
        
        return await _imageUtil.GenerateGachaResultImage(result);
    }
}