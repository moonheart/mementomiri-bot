using System.Linq.Expressions;
using AutoCtor;
using MementoMori.BotServer.Models;
using MementoMori.BotServer.Utils;
using MementoMori.Ortega.Share.Data.Gacha;
using MementoMori.Ortega.Share.Data.Item;
using MementoMori.Ortega.Share.Enums;
using MementoMori.Ortega.Share.Master.Data;

namespace MementoMori.BotServer.MmmrGenerators;

using static Ortega.Share.Masters;

[InjectSingleton]
[AutoConstruct]
public partial class GachaGenerator : GeneratorBase<GachaGenerator>
{
    private readonly ImageUtil _imageUtil;
    private readonly IFreeSql _freeSql;

    public async Task<(byte[] image, string message)> Generate(GachaType gachaType, long userId, int upIndex = 1)
    {
        var upList = GetUpList();
        if (upIndex >= upList.Count) throw new Exception("未找到UP");

        var pickUpCharacterId = upList[upIndex - 1].PickUpCharacterId;

        var items = gachaType switch
        {
            GachaType.PickUp => GetPickUpItemRates(),
            GachaType.Destiny => GetDesnityItemRates(),
            GachaType.Platinum => GetPlatiumItemRates(),
            _ => throw new Exception("未知的抽卡类型")
        };

        var result = new List<GachaResultItem>();
        var random = new Random();
        int alreadyCount = 0;
        bool isSuccess = false;
        var userGachaInfo = await _freeSql.Select<UserGachaInfo>().Where(d => d.Id == userId).ToOneAsync();
        if (userGachaInfo == null)
        {
            userGachaInfo = new UserGachaInfo() {Id = userId, PickUpCount = 0, DestinyCount = 0};
            await _freeSql.Insert(userGachaInfo).ExecuteAffrowsAsync();
        }

        for (var i = 0; i < 10; i++)
        {
            var finalItems = items.Select(d => new GachaItemRate()
            {
                Item = d.Item,
                LotteryRate = d.LotteryRate,
                CharacterRarityFlags = d.CharacterRarityFlags,
                AddItem = d.AddItem
            }).ToList();

            alreadyCount = gachaType switch
            {
                GachaType.PickUp => userGachaInfo.PickUpCount,
                GachaType.Destiny => userGachaInfo.DestinyCount,
                _ => 0
            };

            var (pickUpRate, decrease) = GetPickUpRate(alreadyCount, gachaType, pickUpCharacterId);
            if (pickUpRate != null)
            {
                foreach (var gachaItemRate in finalItems)
                {
                    gachaItemRate.LotteryRate *= decrease;
                }

                finalItems.Add(pickUpRate);
            }

            var rate = random.Next(0, (int) (finalItems.Sum(d => d.LotteryRate) * 10000));
            var sum = 0;
            foreach (var item in finalItems)
            {
                sum += (int) (item.LotteryRate * 10000);
                if (rate <= sum || item == finalItems.Last())
                {
                    var resultItem = new GachaResultItem
                    {
                        ItemCount = item.Item.ItemCount,
                        ItemId = item.Item.ItemId,
                        ItemType = item.Item.ItemType,
                        CharacterRarityFlags = item.CharacterRarityFlags
                    };
                    Expression<Func<UserGachaInfo, int>>? fieldSelector = gachaType switch
                    {
                        GachaType.PickUp => d => d.PickUpCount,
                        GachaType.Destiny => d => d.DestinyCount,
                        _ => null
                    };

                    isSuccess = item.Item.ItemType == ItemType.Character && item.Item.ItemId == pickUpCharacterId;

                    int? value = gachaType switch
                    {
                        GachaType.PickUp when isSuccess => 0,
                        GachaType.PickUp => userGachaInfo.PickUpCount + 1,
                        GachaType.Destiny when isSuccess => 0,
                        GachaType.Destiny => userGachaInfo.DestinyCount + 1,
                        _ => null
                    };

                    if (fieldSelector != null && value != null)
                    {
                        var r = await _freeSql.Update<UserGachaInfo>(userId).Set(fieldSelector, value.Value).ExecuteAffrowsAsync();
                    }

                    switch (gachaType)
                    {
                        case GachaType.Destiny when isSuccess:
                            userGachaInfo.DestinyCount = 0;
                            break;
                        case GachaType.Destiny:
                            userGachaInfo.DestinyCount++;
                            break;
                        case GachaType.PickUp when isSuccess:
                            userGachaInfo.PickUpCount = 0;
                            break;
                        case GachaType.PickUp:
                            userGachaInfo.PickUpCount++;
                            break;
                    }

                    result.Add(resultItem);
                    break;
                }
            }
        }

        var message = gachaType switch
        {
            GachaType.PickUp => $"保底進度：【{(isSuccess ? 0 : alreadyCount + 1)}/100】",
            GachaType.Destiny => $"保底進度：【{(isSuccess ? 0 : alreadyCount + 1)}/70】",
            _ => ""
        };

        return (await _imageUtil.GenerateGachaResultImage(result), message);
    }

    private (GachaItemRate?, double decrease ) GetPickUpRate(int alreadyCount, GachaType gachaType, long upCharacterId)
    {
        var characterMb = CharacterTable.GetById(upCharacterId);
        double? rate = 0;
        double decrease = 0;
        switch (gachaType)
        {
            case GachaType.PickUp:
            {
                rate = alreadyCount == 99 ? 1.0 : 0.0137d;
                break;
            }
            case GachaType.Destiny:
                rate = alreadyCount < 56
                    ? 0.02171d
                    : alreadyCount == 69
                        ? 1
                        : 0.02171d + 0.069877857d * (alreadyCount + 1 - 56);
                decrease = (1 - rate.Value) / (1 - 0.02171d);
                break;
            default: return (null, 0);
        }


        return rate == null
            ? (null, 0)
            : (new()
            {
                Item = new UserItem() {ItemType = ItemType.Character, ItemCount = 1, ItemId = characterMb.Id},
                LotteryRate = rate.Value,
                CharacterRarityFlags = CharacterRarityFlags.SR,
            }, decrease);
    }

    private List<GachaItemRate> GetPickUpItemRates()
    {
        List<GachaItemRate> items = [];
        var nCharacterMbs = CharacterTable.GetArray().Where(d => d.RarityFlags == CharacterRarityFlags.N).ToList(); // N 角色
        var nRarity = 0.5177d / nCharacterMbs.Count;
        AddCharacterItems(nCharacterMbs, items, nRarity, CharacterRarityFlags.N);

        var rCharacterMbs = CharacterTable.GetArray().Where(d => d.RarityFlags == CharacterRarityFlags.R).ToList(); // R 角色
        var rRarity = 0.4377d / rCharacterMbs.Count;
        AddCharacterItems(rCharacterMbs, items, rRarity, CharacterRarityFlags.R);

        HashSet<long> srNormalCharacterIds = [5, 6, 7, 8, 10, 15, 16, 17, 18, 20, 25, 26, 27, 28, 29, 35, 36, 37, 38, 39]; // 20 个常驻 SR
        var srNormalCharacterMbs = CharacterTable.GetArray().Where(d => srNormalCharacterIds.Contains(d.Id)).ToList();
        var srNormalRate = 0.0295d / srNormalCharacterMbs.Count;
        AddCharacterItems(srNormalCharacterMbs, items, srNormalRate, CharacterRarityFlags.SR);

        HashSet<long> srLightDarkCharacterIds = [41, 46]; // 2 个常驻光暗 SR
        var srLightDarkCharacterMbs = CharacterTable.GetArray().Where(d => srLightDarkCharacterIds.Contains(d.Id)).ToList();
        var srLightDarkRate = 0.0014d / srLightDarkCharacterMbs.Count;
        AddCharacterItems(srLightDarkCharacterMbs, items, srLightDarkRate, CharacterRarityFlags.SR);

        return items;
    }

    private List<GachaItemRate> GetDesnityItemRates()
    {
        List<GachaItemRate> items =
        [
            new GachaItemRate()
            {
                CharacterRarityFlags = CharacterRarityFlags.None, LotteryRate = 0.0001, Item = new UserItem() {ItemType = ItemType.CurrencyFree, ItemId = 1, ItemCount = 3000}
            }, // 钻石 30000 None
            new GachaItemRate()
            {
                CharacterRarityFlags = CharacterRarityFlags.R, LotteryRate = 0.0281, Item = new UserItem() {ItemType = ItemType.EquipmentReinforcementItem, ItemId = 1, ItemCount = 300}
            }, // 强化水 300 R
            new GachaItemRate()
            {
                CharacterRarityFlags = CharacterRarityFlags.R, LotteryRate = 0.0281, Item = new UserItem() {ItemType = ItemType.EquipmentReinforcementItem, ItemId = 1, ItemCount = 200}
            }, // 强化水 200 R
            new GachaItemRate()
            {
                CharacterRarityFlags = CharacterRarityFlags.R, LotteryRate = 0.03565, Item = new UserItem() {ItemType = ItemType.EquipmentReinforcementItem, ItemId = 1, ItemCount = 100}
            }, // 强化水 100 R
            new GachaItemRate()
            {
                CharacterRarityFlags = CharacterRarityFlags.SR, LotteryRate = 0.015, Item = new UserItem() {ItemType = ItemType.EquipmentReinforcementItem, ItemId = 2, ItemCount = 4}
            }, // 强化秘药 4 SR
            new GachaItemRate()
            {
                CharacterRarityFlags = CharacterRarityFlags.SR, LotteryRate = 0.03, Item = new UserItem() {ItemType = ItemType.EquipmentReinforcementItem, ItemId = 2, ItemCount = 2}
            }, // 强化秘药 2 SR
            new GachaItemRate()
            {
                CharacterRarityFlags = CharacterRarityFlags.SR, LotteryRate = 0.0936, Item = new UserItem() {ItemType = ItemType.QuestQuickTicket, ItemId = 3, ItemCount = 5}
            }, // 金币(6小时) 5 SR
            new GachaItemRate()
            {
                CharacterRarityFlags = CharacterRarityFlags.SR, LotteryRate = 0.0936, Item = new UserItem() {ItemType = ItemType.QuestQuickTicket, ItemId = 8, ItemCount = 2}
            }, // 经验珠(6小时) 2
            new GachaItemRate()
            {
                CharacterRarityFlags = CharacterRarityFlags.R, LotteryRate = 0.0936, Item = new UserItem() {ItemType = ItemType.QuestQuickTicket, ItemId = 12, ItemCount = 5}
            }, // 潜能宝珠(2小时) 5
            new GachaItemRate()
            {
                CharacterRarityFlags = CharacterRarityFlags.SSR, LotteryRate = 0.045, Item = new UserItem() {ItemType = ItemType.QuestQuickTicket, ItemId = 5, ItemCount = 2}
            }, // 金币(24小时) 2
            new GachaItemRate()
            {
                CharacterRarityFlags = CharacterRarityFlags.SSR, LotteryRate = 0.045, Item = new UserItem() {ItemType = ItemType.QuestQuickTicket, ItemId = 10, ItemCount = 1}
            }, // 经验珠(24小时) 1
            new GachaItemRate()
            {
                CharacterRarityFlags = CharacterRarityFlags.SR, LotteryRate = 0.045, Item = new UserItem() {ItemType = ItemType.QuestQuickTicket, ItemId = 14, ItemCount = 2}
            }, // 潜能宝珠(8小时) 2
            new GachaItemRate()
            {
                CharacterRarityFlags = CharacterRarityFlags.R, LotteryRate = 0.0225, Item = new UserItem() {ItemType = ItemType.TreasureChest, ItemId = 17, ItemCount = 1}
            }, // 魔女的来信(R 蓝) 1
            new GachaItemRate()
            {
                CharacterRarityFlags = CharacterRarityFlags.R, LotteryRate = 0.0225, Item = new UserItem() {ItemType = ItemType.TreasureChest, ItemId = 18, ItemCount = 1}
            }, // 魔女的来信(R 红) 1
            new GachaItemRate()
            {
                CharacterRarityFlags = CharacterRarityFlags.R, LotteryRate = 0.0225, Item = new UserItem() {ItemType = ItemType.TreasureChest, ItemId = 19, ItemCount = 1}
            }, // 魔女的来信(R 绿) 1
            new GachaItemRate()
            {
                CharacterRarityFlags = CharacterRarityFlags.R, LotteryRate = 0.0225, Item = new UserItem() {ItemType = ItemType.TreasureChest, ItemId = 20, ItemCount = 1}
            }, // 魔女的来信(R 黄) 1
            new GachaItemRate()
            {
                CharacterRarityFlags = CharacterRarityFlags.SR, LotteryRate = 0.008, Item = new UserItem() {ItemType = ItemType.TreasureChest, ItemId = 21, ItemCount = 1}
            }, // 魔女的来信(SR 蓝) 1
            new GachaItemRate()
            {
                CharacterRarityFlags = CharacterRarityFlags.SR, LotteryRate = 0.008, Item = new UserItem() {ItemType = ItemType.TreasureChest, ItemId = 22, ItemCount = 1}
            }, // 魔女的来信(SR 红) 1
            new GachaItemRate()
            {
                CharacterRarityFlags = CharacterRarityFlags.SR, LotteryRate = 0.008, Item = new UserItem() {ItemType = ItemType.TreasureChest, ItemId = 23, ItemCount = 1}
            }, // 魔女的来信(SR 绿) 1
            new GachaItemRate()
            {
                CharacterRarityFlags = CharacterRarityFlags.SR, LotteryRate = 0.008, Item = new UserItem() {ItemType = ItemType.TreasureChest, ItemId = 24, ItemCount = 1}
            }, // 魔女的来信(SR 黄) 1
            new GachaItemRate()
            {
                CharacterRarityFlags = CharacterRarityFlags.SR, LotteryRate = 0.02, Item = new UserItem() {ItemType = ItemType.MatchlessSacredTreasureExpItem, ItemId = 1, ItemCount = 1}
            }, // 魔装香油 1
            new GachaItemRate()
            {
                CharacterRarityFlags = CharacterRarityFlags.SR, LotteryRate = 0.0728, Item = new UserItem() {ItemType = ItemType.TreasureChest, ItemId = 13, ItemCount = 1}
            }, // 圣德芬的卷轴 1
            new GachaItemRate()
            {
                CharacterRarityFlags = CharacterRarityFlags.SR, LotteryRate = 0.0728, Item = new UserItem() {ItemType = ItemType.TreasureChest, ItemId = 14, ItemCount = 1}
            }, // 圣德芬的魔书 1
            new GachaItemRate()
            {
                CharacterRarityFlags = CharacterRarityFlags.SSR, LotteryRate = 0.0306, Item = new UserItem() {ItemType = ItemType.TreasureChest, ItemId = 15, ItemCount = 1}
            }, // 亚斯塔绿的卷轴 1
            new GachaItemRate()
            {
                CharacterRarityFlags = CharacterRarityFlags.SSR, LotteryRate = 0.0306, Item = new UserItem() {ItemType = ItemType.TreasureChest, ItemId = 16, ItemCount = 1}
            }, // 亚斯塔绿的魔书 1
            new GachaItemRate()
            {
                CharacterRarityFlags = CharacterRarityFlags.SR, LotteryRate = 0.01917, Item = new UserItem() {ItemType = ItemType.BossChallengeTicket, ItemId = 1, ItemCount = 1}
            }, // 首领挑战券 2
            new GachaItemRate()
            {
                CharacterRarityFlags = CharacterRarityFlags.SR, LotteryRate = 0.01917, Item = new UserItem() {ItemType = ItemType.TowerBattleTicket, ItemId = 1, ItemCount = 1}
            }, // 无穷之塔挑战券 2
            new GachaItemRate()
            {
                CharacterRarityFlags = CharacterRarityFlags.UR, LotteryRate = 0.03836, Item = new UserItem() {ItemType = ItemType.ExchangePlaceItem, ItemId = 1, ItemCount = 1}
            }, // 魔水晶 1
        ];
        return items;
    }

    private List<GachaItemRate> GetPlatiumItemRates()
    {
        List<GachaItemRate> items = [];

        var nCharacterMbs = CharacterTable.GetArray().Where(d => d.RarityFlags == CharacterRarityFlags.N).ToList(); // N 角色
        var nRarity = 0.5177d / nCharacterMbs.Count;
        AddCharacterItems(nCharacterMbs, items, nRarity, CharacterRarityFlags.N);

        var rCharacterMbs = CharacterTable.GetArray().Where(d => d.RarityFlags == CharacterRarityFlags.R).ToList(); // R 角色
        var rRarity = 0.4377d / rCharacterMbs.Count;
        AddCharacterItems(rCharacterMbs, items, rRarity, CharacterRarityFlags.R);

        HashSet<long> srNormalCharacterIds = [5, 6, 7, 8, 10, 15, 16, 17, 18, 20, 25, 26, 27, 28, 29, 35, 36, 37, 38, 39]; // 20 个常驻 SR
        var srNormalCharacterMbs = CharacterTable.GetArray().Where(d => srNormalCharacterIds.Contains(d.Id)).ToList();
        var srNormalRate = 0.0426d / srNormalCharacterMbs.Count;
        AddCharacterItems(srNormalCharacterMbs, items, srNormalRate, CharacterRarityFlags.SR);

        HashSet<long> srLightDarkCharacterIds = [41, 46]; // 2 个常驻光暗 SR
        var srLightDarkCharacterMbs = CharacterTable.GetArray().Where(d => srLightDarkCharacterIds.Contains(d.Id)).ToList();
        var srLightDarkRate = 0.002d / srLightDarkCharacterMbs.Count;
        AddCharacterItems(srLightDarkCharacterMbs, items, srLightDarkRate, CharacterRarityFlags.SR);

        return items;
    }

    public List<GachaCaseUiMB> GetUpList()
    {
        List<GachaCaseUiMB> res = [];
        var now = DateTimeOffset.Now;
        foreach (var gachaCaseMb in GachaCaseTable.GetArray())
        {
            var startTime = new DateTimeOffset(DateTime.Parse(gachaCaseMb.StartTimeFixJST), TimeSpan.FromHours(9));
            var endTime = new DateTimeOffset(DateTime.Parse(gachaCaseMb.EndTimeFixJST), TimeSpan.FromHours(9));
            if (now < startTime || now > endTime) continue;
            var gachaCaseUiMb = GachaCaseUiTable.GetById(gachaCaseMb.GachaCaseUiId);
            if (gachaCaseUiMb.PickUpCharacterId > 0)
            {
                res.Add(gachaCaseUiMb);
            }
        }

        return res;
    }

    private void AddCharacterItems(List<CharacterMB> nCharacterMbs, List<GachaItemRate> items, double nRarity, CharacterRarityFlags rarityFlags)
    {
        foreach (var characterMb in nCharacterMbs)
        {
            items.Add(new()
            {
                Item = new UserItem() {ItemType = ItemType.Character, ItemCount = 1, ItemId = characterMb.Id},
                LotteryRate = nRarity,
                CharacterRarityFlags = rarityFlags,
            });
        }
    }
}