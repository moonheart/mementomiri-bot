﻿using System.Text;
using System.Text.RegularExpressions;
using AutoCtor;
using EleCho.GoCqHttpSdk;
using EleCho.GoCqHttpSdk.Message;
using EleCho.GoCqHttpSdk.MessageMatching;
using EleCho.GoCqHttpSdk.Post;
using MementoMori.BotServer.Api;
using MementoMori.BotServer.MmmrGenerators;
using MementoMori.BotServer.Models;
using MementoMori.BotServer.Options;
using MementoMori.BotServer.Utils;
using MementoMori.Extensions;
using MementoMori.Option;
using MementoMori.Ortega;
using MementoMori.Ortega.Share;
using MementoMori.Ortega.Share.Data.ApiInterface.Auth;
using MementoMori.Ortega.Share.Data.ApiInterface.Notice;
using MementoMori.Ortega.Share.Data.Notice;
using MementoMori.Ortega.Share.Enums;
using MementoMori.Ortega.Share.Master.Data;
using Newtonsoft.Json.Linq;
using Ortega.Common.Manager;
using Refit;
using static MementoMori.Ortega.Share.Masters;

namespace MementoMori.BotServer.Plugins;

[InjectSingleton]
[AutoConstruct]
public partial class MementoMoriQueryPlugin : CqMessageMatchPostPlugin
{
    private readonly IWritableOptions<BotOptions> _botOptions;
    private readonly SessionAccessor _sessionAccessor;
    private IMentemoriIcu _mentemoriIcu;
    private readonly MementoNetworkManager _networkManager;
    private readonly ILogger<MementoMoriQueryPlugin> _logger;
    private readonly IFreeSql _fsql;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ImageUtil _imageUtil;
    private readonly SkillGenerator _skillGenerator;
    private readonly GachaGenerator _gachaGenerator;

    [AutoPostConstruct]
    public void MementoMoriQueryPlugin1()
    {
        _mentemoriIcu = RestService.For<IMentemoriIcu>(_botOptions.Value.MentemoriIcuUri);
        _ = AutoNotice();
        _ = AutoDmmVersionCheck();
        // _networkManager.MoriHttpClientHandler.WhenAnyValue(d => d.OrtegaMasterVersion).Subscribe(NotifyNewMasterVersion);
        // _networkManager.MoriHttpClientHandler.WhenAnyValue(d => d.OrtegaAssetVersion).Subscribe(NotifyNewAssetVersion);
    }

    private void NotifyNewMasterVersion(string masterVersion)
    {
        while (!_sessionAccessor.Session.IsConnected)
        {
            Thread.Sleep(TimeSpan.FromSeconds(1));
        }

        var msg = $"发现新的 Ortega Master 版本 {masterVersion}";
        foreach (var group in _botOptions.Value.OpenedGroups)
        {
            _sessionAccessor.Session.SendGroupMessage(group, new CqMessage(msg));
        }
    }

    private void NotifyNewAssetVersion(string assetVersion)
    {
        while (!_sessionAccessor.Session.IsConnected)
        {
            Thread.Sleep(TimeSpan.FromSeconds(1));
        }

        var msg = $"发现新的 Ortega Asset 版本 {assetVersion}";
        foreach (var group in _botOptions.Value.OpenedGroups)
        {
            _sessionAccessor.Session.SendGroupMessage(group, new CqMessage(msg));
        }
    }

    private async Task AutoNotice()
    {
        await Task.Delay(TimeSpan.FromSeconds(10));
        var apiAuth = _botOptions.Value.NoticeApiAuth;
        while (true)
        {
            try
            {
                await Login();
                await GetNotice(NoticeAccessType.Title, option => option.LastNotices);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "get notice failed");
            }
            finally
            {
                await Task.Delay(TimeSpan.FromMinutes(10));
            }
        }

        async Task GetNotice(NoticeAccessType noticeAccessType, Func<BotOptions, List<long>> getList)
        {
            _logger.LogInformation("start retrieve notices");
            var listResponse = await _networkManager.GetResponse<GetNoticeInfoListRequest, GetNoticeInfoListResponse>(new GetNoticeInfoListRequest()
            {
                AccessType = noticeAccessType,
                CountryCode = OrtegaConst.Addressable.LanguageNameDictionary[_networkManager.LanguageType],
                LanguageType = _networkManager.LanguageType,
                UserId = 1
            }, apiAuth: new Uri(apiAuth));

            var noticeInfos = listResponse.NoticeInfoList.Concat(listResponse.EventInfoList).OrderByDescending(d => d.Id).ToList();
            var noticeToPush = new List<NoticeInfo>();
            foreach (var noticeInfo in noticeInfos)
            {
                if (await _fsql.Select<NoticeInfo>().AnyAsync(d => d.Id == noticeInfo.Id))
                {
                    continue;
                }

                noticeToPush.Add(noticeInfo);
                await _fsql.Insert(noticeInfo).ExecuteAffrowsAsync();
            }

            // only send latest 5
            foreach (var noticeInfo in noticeToPush.Where(d => d.Id % 10 != 6).Take(5))
            {
                var msg = new StringBuilder();
                msg.AppendLine($"<h1>{noticeInfo.Title}({noticeInfo.Id})</h1>");
                msg.AppendLine();
                var mainText = $"<div>{noticeInfo.MainText}</div>"; //.Replace("<br>", "\r\n");
                msg.AppendLine(mainText);
                var bytes = _imageUtil.HtmlToImage(msg.ToString());
                foreach (var group in _botOptions.Value.OpenedGroups)
                {
                    // await Task.Delay(TimeSpan.FromSeconds(1));
                    _logger.LogInformation($"send notice{noticeInfo.Title} to group {group}");
                    var cqImageMsg = CqImageMsg.FromBytes(bytes);
                    await _sessionAccessor.Session.SendGroupMessageAsync(group, new CqMessage(cqImageMsg, new CqTextMsg(noticeInfo.Title)));
                }
            }
        }

        async Task Login()
        {
            if (_networkManager.UserId > 0) return;
            var createUserResponse = await _networkManager.GetResponse<CreateUserRequest, CreateUserResponse>(new CreateUserRequest
            {
                AdverisementId = "",
                AppVersion = "9999.99.0",
                CountryCode = "TW",
                DeviceToken = "",
                ModelName = "pixel",
                DisplayLanguage = LanguageType.zhTW,
                OSVersion = "Android 14",
                SteamTicket = "",
                AuthToken = await GetAuthToken()
            } ,apiAuth: new Uri(apiAuth));
            _networkManager.UserId = createUserResponse.UserId;
        }

        async Task<int> GetAuthToken()
        {
            try
            {
                var json = await _httpClientFactory.CreateClient().GetStringAsync("https://list.moonheart.dev/d/public/mmtm/AddressableLocalAssets/ScriptableObjects/AuthToken/AuthTokenData.json?v=" + DateTimeOffset.Now.ToUnixTimeMilliseconds());
                return JObject.Parse(json)["_authToken"]?.Value<int>() ?? 0;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "failed to get authToken");
                return 0;
            }
        }
    }

    private async Task AutoDmmVersionCheck()
    {
        await Task.Delay(TimeSpan.FromSeconds(10));
        while (true)
        {
            try
            {
                var httpClient = _httpClientFactory.CreateClient();
                while (true)
                {
                    await Task.Delay(1000);
                    var dmmGameId = _botOptions.Value.LastDmmGameId + 1;
                    var dmmUrl = $"{_botOptions.Value.DmmApiUrl}/gameplayer/filelist/{dmmGameId}";
                    var json = await httpClient.GetStringAsync(dmmUrl);
                    var jObject = JObject.Parse(json);
                    if (jObject["result_code"]?.Value<long>() != 100) break;
                    if (jObject["data"]?["file_list"] is not JArray jArray || jArray.Count == 0) break;

                    _botOptions.Update(d => d.LastDmmGameId = dmmGameId);
                    // product/mementomori/MementoMori/content/win/2.3.0/data/GameAssembly.dll
                    var match = Regex.Match(jArray[0]["path"]?.ToString() ?? "", @"product/mementomori/MementoMori/content/win/(?<version>.+?)/data/");
                    if (!match.Success) continue;

                    var version = match.Groups["version"].Value;
                    foreach (var group in _botOptions.Value.OpenedGroups)
                    {
                        await _sessionAccessor.Session.SendGroupMessageAsync(group, new CqMessage($"DMM 版本监测：发现新版 {version} ({dmmGameId})"));
                    }
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "get dmm version failed");
            }
            finally
            {
                await Task.Delay(TimeSpan.FromMinutes(10));
            }
        }
    }

    private bool IsGroupAllowed(CqGroupMessagePostContext context)
    {
        return _botOptions.Value.OpenedGroups.Contains(context.GroupId);
    }

    [CqMessageMatch("^/(命令列表|cmds)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    public async Task FunctionList(CqGroupMessagePostContext context)
    {
        if (!IsGroupAllowed(context)) return;
        _logger.LogInformation(nameof(FunctionList));
        var msg = new StringBuilder();
        msg.AppendLine("命令列表");
        msg.AppendLine("/命令列表|cmds");
        msg.AppendLine("/角色ID列表|ids");
        msg.AppendLine("/速度列表|speed");
        msg.AppendLine("/技能|skill 角色ID (示例 /技能 1)");
        msg.AppendLine("/角色|cha 角色ID (示例 /角色 1)");
        msg.AppendLine("/主线|q 关卡 (示例 /主线 12-28)");
        msg.AppendLine("/(无穷|i|红|r|黄|y|绿|g|蓝|b)塔|t 关卡 (示例 /绿塔 499)");
        msg.AppendLine("/(战力|bp|等级|lv|主线|q|塔|t|竞技场|pvp)排名|rank (日|jp|韩|kr|亚|as|美|us|欧|eu|国际|gl)1 (示例 /战力排名 日10)");
        msg.AppendLine("/公告|notice [ID] (示例 /公告 123 ，/公告)");
        msg.AppendLine("/头像|ava [稀有度] [元素|b|g|r|y|l|d] [等级] [@xxx](/头像 lr+7 光 lv333 @xxx)");
        msg.AppendLine("/抽卡|gacha (列表|list|up1|命运|des1|白金|pla)(/抽卡up1)");
        await _sessionAccessor.Session.SendGroupMessageAsync(context.GroupId, new CqMessage(msg.ToString()));
    }

    // 通过 CqMessageMatch 来指定匹配规则 (例如这里非贪婪匹配两个中括号之间的任意内容, 并命名为 content 组)
    [CqMessageMatch(@"^/(角色ID列表|ids)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    public async Task QueryCharacterIds(CqGroupMessagePostContext context)
    {
        if (!IsGroupAllowed(context)) return;
        _logger.LogInformation(nameof(QueryCharacterIds));
        var msg = new StringBuilder();
        var i = 1;
        foreach (var characterMb in CharacterTable.GetArray())
        {
            var name = CharacterTable.GetCharacterName(characterMb.Id);
            msg.AppendLine($"{characterMb.Id:000}: {name}");
        }

        await _sessionAccessor.Session.SendGroupMessageAsync(context.GroupId, new CqMessage(msg.ToString()));
    }

    [CqMessageMatch(@"^/(技能|skill)\s*(?<idStr>\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    public async Task QueryCharacterSkills(CqGroupMessagePostContext context, string idStr)
    {
        if (!IsGroupAllowed(context)) return;
        _logger.LogInformation($"{nameof(QueryCharacterSkills)} {idStr}");
        var id = long.Parse(idStr);

        if (_skillGenerator.TryGenerate(id, out var image, out var err))
        {
            var cqImageMsg = CqImageMsg.FromBytes(image);
            await _sessionAccessor.Session.SendGroupMessageAsync(context.GroupId, new CqMessage(cqImageMsg));
        }
        else
        {
            await _sessionAccessor.Session.SendGroupMessageAsync(context.GroupId, new CqMessage(err));
        }
    }

    [CqMessageMatch(@"^/(速度列表|speed)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    public async Task QuerySpeedList(CqGroupMessagePostContext context)
    {
        if (!IsGroupAllowed(context)) return;
        _logger.LogInformation($"{nameof(QuerySpeedList)}");

        var msg = new StringBuilder();
        foreach (var d in CharacterTable.GetArray().Select(characterMb =>
                 {
                     var chaName = CharacterTable.GetCharacterName(characterMb.Id);
                     var speed = characterMb.InitialBattleParameter.Speed;
                     return new {chaName, speed};
                 }).OrderByDescending(d => d.speed))
        {
            msg.AppendLine($"{d.speed}: {d.chaName}");
        }

        await _sessionAccessor.Session.SendGroupMessageAsync(context.GroupId, new CqMessage(msg.ToString()));
    }

    [CqMessageMatch(@"^/(角色|cha)\s*(?<idStr>\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    public async Task QueryCharacter(CqGroupMessagePostContext context, string idStr)
    {
        if (!IsGroupAllowed(context)) return;
        _logger.LogInformation($"{nameof(QueryCharacter)} {idStr}");
        var id = long.Parse(idStr);
        var msg = new StringBuilder();
        var profile = CharacterProfileTable.GetById(id);
        if (profile == null)
        {
            await _sessionAccessor.Session.SendGroupMessageAsync(context.GroupId, new CqMessage("未找到角色"));
            return;
        }

        var characterMb = CharacterTable.GetById(id);

        var characterName = CharacterTable.GetCharacterName(id);
        msg.AppendLine($"{characterName} (ID: {id})");
        msg.AppendLine($"元素: {TextResourceTable.Get(characterMb.ElementType)}");
        msg.AppendLine($"职业: {TextResourceTable.Get(characterMb.JobFlags)}");
        msg.AppendLine($"速度: {characterMb.InitialBattleParameter.Speed}");
        msg.AppendLine($"身高: {profile.Height}cm");
        msg.AppendLine($"体重: {profile.Weight}kg");
        msg.AppendLine($"血型: {profile.BloodType}");
        var birthDay = profile.Birthday % 100;
        var birthMonth = profile.Birthday / 100;
        msg.AppendLine($"生日: {birthMonth}月{birthDay}日");
        msg.AppendLine($"声优: {TextResourceTable.Get(profile.CharacterVoiceJPKey)}");
        msg.AppendLine($"演唱: {TextResourceTable.Get(profile.VocalJPKey)}");
        msg.AppendLine($"抒情诗: {TextResourceTable.Get(profile.LamentJPKey)}");
        msg.AppendLine($"{TextResourceTable.Get(profile.DescriptionKey)}");

        await _sessionAccessor.Session.SendGroupMessageAsync(context.GroupId, new CqMessage(msg.ToString()));
    }

    private const string tableStyle = """
                                      <style>
                                      table {
                                        font-family: arial, sans-serif;
                                        border-collapse: collapse;
                                        width: 100%;
                                      }

                                      td, th {
                                        border: 1px solid #dddddd;
                                        text-align: left;
                                        padding: 4px;
                                      }
                                      </style>
                                      """;

    [CqMessageMatch(@"^/(主线|q)\s*(?<quest>\d+-\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    public async Task QueryMainQuerst(CqGroupMessagePostContext context, string quest)
    {
        if (!IsGroupAllowed(context)) return;
        _logger.LogInformation($"{nameof(QueryMainQuerst)} {quest}");

        var questMb = QuestTable.GetArray().FirstOrDefault(d => d.Memo == quest);
        if (questMb == null)
        {
            await _sessionAccessor.Session.SendGroupMessageAsync(context.GroupId, new CqMessage("未找到关卡"));
            return;
        }

        var msg = new StringBuilder(tableStyle);
        msg.AppendLine($"<h1>主线 {quest}</h1> <span>战斗力: {CalcNumber(questMb.BaseBattlePower)}</span> <span>潜能宝珠: {CalcNumber(questMb.PotentialJewelPerDay)}</span>");
        var enemies = new List<BossBattleEnemyMB>();
        for (var i = 1; i < 6; i++)
        {
            var enemyId = 20000000 + questMb.Id * 100 + i;
            enemies.Add(BossBattleEnemyTable.GetById(enemyId));
        }

        BuildEnemyInfo(enemies, msg);

        var bytes = _imageUtil.HtmlToImage(msg.ToString(), 1200);

        var cqImageMsg = CqImageMsg.FromBytes(bytes);
        await _sessionAccessor.Session.SendGroupMessageAsync(context.GroupId, new CqMessage(cqImageMsg));
    }

    private static void BuildEnemyInfo(IReadOnlyList<IBattleEnemy> enemies, StringBuilder msg)
    {
        msg.AppendLine(@"<table><tr>
<th>稀有</th>
<th>Lv</th>
<th>名称</th>
<th>素</th>
<th>速度</th>
<th>共鸣</th>
<th>攻击</th>
<th>防御</th>
<th>血量</th>
<th>力量</th>
<th>技力</th>
<th>魔力</th>
<th>耐力</th>
</tr>");
        foreach (var enemyMb in enemies)
        {
            // msg.AppendLine();
            var rarity = enemyMb.CharacterRarityFlags.GetDesc();
            var lv = enemyMb.EnemyRank;
            var name = TextResourceTable.Get(enemyMb.NameKey);
            var ele = enemyMb.ElementType.GetDesc();
            var connect = "";
            if (enemies.MaxBy(d => d.BattleParameter.Defense) == enemyMb)
            {
                connect = $"高";
            }
            else if (enemies.MinBy(d => d.BattleParameter.Defense) == enemyMb)
            {
                connect = $"低";
            }

            msg.AppendLine(@$"<tr>
<td>{rarity}</td><td>{lv}</td>
<td>{name}</td><td>{ele}</td>
<td>{enemyMb.BattleParameter.Speed}</td>
<td>{connect}</td>
<td>{CalcNumber(enemyMb.BattleParameter.AttackPower)}</td>
<td>{CalcNumber(enemyMb.BattleParameter.Defense)}</td>
<td>{CalcNumber(enemyMb.BattleParameter.HP)}</td>
<td>{CalcNumber(enemyMb.BaseParameter.Muscle)}</td>
<td>{CalcNumber(enemyMb.BaseParameter.Energy)}</td>
<td>{CalcNumber(enemyMb.BaseParameter.Intelligence)}</td>
<td>{CalcNumber(enemyMb.BaseParameter.Health)}</td>
</tr>");
        }

        msg.AppendLine("</table>");
    }

    /// <summary>
    /// 将一个数字转换为可读的字符串，如 1000000 -> 100万，1000000000 -> 10亿，可以设置精确到的小数点位数
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    private static string CalcNumber(long value, int precision = 0)
    {
        var unit = new[] {"", "万", "亿", "万亿"};
        var index = 0;
        var d = (double) value;
        while (d >= 10000)
        {
            d /= 10000;
            index++;
        }

        var num = d.ToString($"F7");
        num = num.Substring(0, 5);
        // 去掉最后的小数点
        if (num.EndsWith("."))
        {
            num = num.Substring(0, num.Length - 1);
        }

        return $"{num}{unit[index]}";
    }

    [CqMessageMatch(@"^/(?<towerTypeStr>(无穷|i|红|r|黄|y|金|绿|g|翠|b|蓝))(塔|t)\s*(?<quest>\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    public async Task QueryTowerInfo(CqGroupMessagePostContext context, string towerTypeStr, string quest)
    {
        if (!IsGroupAllowed(context)) return;
        _logger.LogInformation($"{nameof(QueryTowerInfo)} {towerTypeStr} {quest}");

        var englishMap = new Dictionary<string, string>
        {
            {"i", "无穷"},
            {"r", "红"},
            {"y", "黄"},
            {"g", "绿"},
            {"b", "蓝"}
        };

        var towerType = towerTypeStr switch
        {
            "无穷" or "i" => TowerType.Infinite,
            "红" or "r" => TowerType.Red,
            "黄" or "金" or "y" => TowerType.Yellow,
            "绿" or "翠" or "g" => TowerType.Green,
            "蓝" or "b" => TowerType.Blue,
        };
        var questId = long.Parse(quest);

        var questMb = TowerBattleQuestTable.GetByTowerTypeAndFloor(questId, towerType);
        if (questMb == null)
        {
            await _sessionAccessor.Session.SendGroupMessageAsync(context.GroupId, new CqMessage("未找到关卡"));
            return;
        }

        var msg = new StringBuilder(tableStyle);
        msg.AppendLine($"<h1>{englishMap.GetValueOrDefault(towerTypeStr, towerTypeStr)}塔 {quest}</h1>");
        var enemies = new List<TowerBattleEnemyMB>();
        foreach (var enemyId in questMb.EnemyIds)
        {
            enemies.Add(TowerBattleEnemyTable.GetById(enemyId));
        }

        BuildEnemyInfo(enemies, msg);

        var bytes = _imageUtil.HtmlToImage(msg.ToString(), 1200);

        var cqImageMsg = CqImageMsg.FromBytes(bytes);
        await _sessionAccessor.Session.SendGroupMessageAsync(context.GroupId, new CqMessage(cqImageMsg));
    }

    [CqMessageMatch(@"^/(?<rankType>战力|bp|等级|lv|主线|q|塔|t)(排名|rank)\s*(?<server>日|jp|韩|kr|亚|as|美|us|欧|eu|国际|gl)(?<worldStr>\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    public async Task GetPlayerRanking(CqGroupMessagePostContext context, string rankType, string server, string worldStr)
    {
        if (!IsGroupAllowed(context)) return;
        _logger.LogInformation($"{nameof(GetPlayerRanking)} {rankType} {server} {worldStr}");

        var serverMap = new Dictionary<string, string>
        {
            {"日", "日服"},
            {"jp", "日服"},
            {"韩", "韩服"},
            {"kr", "韩服"},
            {"亚", "亚服"},
            {"as", "亚服"},
            {"美", "美服"},
            {"us", "美服"},
            {"欧", "欧服"},
            {"eu", "欧服"},
            {"国际", "国际服"},
            {"gl", "国际服"},
        };

        var rankTypeMap = new Dictionary<string, string>
        {
            {"bp", "战力"},
            {"lv", "等级"},
            {"q", "主线"},
            {"t", "塔"},
        };

        var world = int.Parse(worldStr);
        var worldId = server switch
        {
            "日" or "jp" => 1000 + world,
            "韩" or "kr" => 2000 + world,
            "亚" or "as" => 3000 + world,
            "美" or "us" => 4000 + world,
            "欧" or "eu" => 5000 + world,
            "国际" or "gl" => 6000 + world,
            _ => 1000 + world,
        };

        var playerRanking = await _mentemoriIcu.PlayerRanking(worldId);
        if (playerRanking.status != 200)
        {
            await _sessionAccessor.Session.SendGroupMessageAsync(context.GroupId, new CqMessage($"获取失败：{playerRanking.status}"));
            return;
        }

        var msg = new StringBuilder(tableStyle);
        msg.AppendLine($"<h1>{serverMap.GetValueOrDefault(server, server)}{world} {rankTypeMap.GetValueOrDefault(rankType, rankType)}排名</h1>");
        var infos = rankType switch
        {
            "战力" or "bp" => playerRanking.data.rankings.bp,
            "等级" or "lv" => playerRanking.data.rankings.rank,
            "主线" or "q" => playerRanking.data.rankings.quest,
            "塔" or "t" => playerRanking.data.rankings.tower,
            _ => playerRanking.data.rankings.bp
        };
        Func<PlayerInfo, string> selector = rankType switch
        {
            "战力" or "bp" => p => p.bp.ToString("N0"),
            "等级" or "lv" => p => p.rank.ToString(),
            "主线" or "q" => p => QuestTable.GetById(p.quest_id).Memo,
            "塔" or "t" => p => p.tower_id.ToString(),
            _ => p => p.bp.ToString("N0")
        };
        msg.AppendLine("<table><tbody>");
        for (var i = 0; i < infos.Count; i++)
        {
            var playerInfo = infos[i];
            msg.AppendLine($"<tr><td>No.{i + 1:00}</td><td>{playerInfo.name}</td><td>{selector(playerInfo)}</td></tr>");
        }

        msg.AppendLine("</tbody></table>");

        var bytes = _imageUtil.HtmlToImage(msg.ToString(), null);

        var cqImageMsg = CqImageMsg.FromBytes(bytes);
        await _sessionAccessor.Session.SendGroupMessageAsync(context.GroupId, new CqMessage(cqImageMsg));
        // await _sessionAccessor.Session.SendGroupMessageAsync(context.GroupId, new CqMessage(msg.ToString()));
    }

    [CqMessageMatch(@"^/(竞技场|pvp)(排名|rank)\s*(?<server>日|jp|韩|kr|亚|as|美|us|欧|eu|国际|gl)(?<worldStr>\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    public async Task GetArenaRanking(CqGroupMessagePostContext context, string server, string worldStr)
    {
        if (!IsGroupAllowed(context)) return;
        _logger.LogInformation($"{nameof(GetArenaRanking)} {server} {worldStr}");

        var serverMap = new Dictionary<string, string>
        {
            {"日", "日服"},
            {"jp", "日服"},
            {"韩", "韩服"},
            {"kr", "韩服"},
            {"亚", "亚服"},
            {"as", "亚服"},
            {"美", "美服"},
            {"us", "美服"},
            {"欧", "欧服"},
            {"eu", "欧服"},
            {"国际", "国际服"},
            {"gl", "国际服"},
        };

        var world = int.Parse(worldStr);
        var worldId = server switch
        {
            "日" or "jp" => 1000 + world,
            "韩" or "kr" => 2000 + world,
            "亚" or "as" => 3000 + world,
            "美" or "us" => 4000 + world,
            "欧" or "eu" => 5000 + world,
            "国际" or "gl" => 6000 + world,
            _ => 1000 + world,
        };

        var arena = await _mentemoriIcu.Arena(worldId);

        var msg = new StringBuilder();
        msg.AppendLine($"{serverMap.GetValueOrDefault(server, server)}{worldStr} 竞技场排名");

        for (var i = 0; i < arena.data.Count; i++)
        {
            var arenaInfo = arena.data[i];
            msg.AppendLine($"No.{i + 1:000}\t{arenaInfo.PlayerName}(Lv.{arenaInfo.PlayerLevel})");
        }

        await _sessionAccessor.Session.SendGroupMessageAsync(context.GroupId, new CqMessage(msg.ToString()));
    }

    [CqMessageMatch(@"^/(公告|notice)\s*?(?<noticeIdStr>\d+)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    public async Task RecentNotice(CqGroupMessagePostContext context, string? noticeIdStr)
    {
        if (!IsGroupAllowed(context)) return;
        _logger.LogInformation($"{nameof(RecentNotice)} {noticeIdStr}");
        CqMessage msg;
        if (int.TryParse(noticeIdStr, out var noticeId))
        {
            var noticeInfo = noticeId is > 0 and <= 20
                ? (await _fsql.Select<NoticeInfo>().OrderByDescending(d => d.Id).Take(30).ToListAsync()).Where(d => d.Id % 10 != 6).Skip(noticeId - 1).FirstOrDefault()
                : await _fsql.Select<NoticeInfo>().Where(d => d.Id == noticeId).FirstAsync();
            if (noticeInfo == null)
            {
                msg = new CqMessage("未找到此公告");
            }
            else
            {
                var notice = new StringBuilder();
                notice.AppendLine($"<h1>{noticeInfo.Title}({noticeInfo.Id})</h1>");
                notice.AppendLine();
                var mainText = $"<div>{noticeInfo.MainText}</div>";
                notice.AppendLine(mainText);
                var bytes = _imageUtil.HtmlToImage(notice.ToString());
                var cqImageMsg = CqImageMsg.FromBytes(bytes);
                msg = new CqMessage(cqImageMsg, new CqTextMsg(noticeInfo.Title));
            }
        }
        else
        {
            var noticeInfos = await _fsql.Select<NoticeInfo>().OrderByDescending(d => d.Id).Limit(30).ToListAsync();
            var list = new StringBuilder();
            for (var i = 0; i < noticeInfos.Where(d => d.Id % 10 != 6).Take(15).ToArray().Length; i++)
            {
                var noticeInfo = noticeInfos.Where(d => d.Id % 10 != 6).Take(15).ToArray()[i];
                list.AppendLine($"({i + 1}) {noticeInfo.Title}");
            }

            msg = new CqMessage(list.ToString());
        }

        await _sessionAccessor.Session.SendGroupMessageAsync(context.GroupId, msg);
    }

    [CqMessageMatch(@"^\/(头像|ava)\s*(?:(?<rarity>n|(?:s|ss|u|l)?r)(?<plus>\+)?(?<numberStr>\d)?)?\s*(?<elementStr>(蓝|b|红|r|绿|g|黄|y|光|l|暗|d))?\s*(lv(?<lvStr>\d+))?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    public async Task GenerateAvatar(CqGroupMessagePostContext context, string rarity, string plus, string numberStr, string elementStr, string? lvStr)
    {
        if (plus == "+" && rarity.Equals("n", StringComparison.OrdinalIgnoreCase)) return;
        if (numberStr != "" && !rarity.Equals("lr", StringComparison.OrdinalIgnoreCase)) return;
        var number = 0;
        if (numberStr != "" && !int.TryParse(numberStr, out number) || number > 10 || number < 0) return;
        var lv = 1;
        if (lvStr != "" && !int.TryParse(lvStr, out lv) || lv > 999 || lv < 1) return;

        var characterRarity = (rarity, plus, number) switch
        {
            ("lr", "+", 10) => CharacterRarityFlags.LRPlus10,
            ("lr", "+", 9) => CharacterRarityFlags.LRPlus9,
            ("lr", "+", 8) => CharacterRarityFlags.LRPlus8,
            ("lr", "+", 7) => CharacterRarityFlags.LRPlus7,
            ("lr", "+", 6) => CharacterRarityFlags.LRPlus6,
            ("lr", "+", 5) => CharacterRarityFlags.LRPlus5,
            ("lr", "+", 4) => CharacterRarityFlags.LRPlus4,
            ("lr", "+", 3) => CharacterRarityFlags.LRPlus3,
            ("lr", "+", 2) => CharacterRarityFlags.LRPlus2,
            ("lr", "+", _) => CharacterRarityFlags.LRPlus,
            ("lr", "", _) => CharacterRarityFlags.LR,
            ("ur", "+", _) => CharacterRarityFlags.URPlus,
            ("ur", "", _) => CharacterRarityFlags.UR,
            ("ssr", "+", _) => CharacterRarityFlags.SSRPlus,
            ("ssr", "", _) => CharacterRarityFlags.SSR,
            ("sr", "+", _) => CharacterRarityFlags.SRPlus,
            ("sr", "", _) => CharacterRarityFlags.SR,
            ("r", "+", _) => CharacterRarityFlags.RPlus,
            ("r", "", _) => CharacterRarityFlags.R,
            ("n", "", _) => CharacterRarityFlags.N,
            _ => CharacterRarityFlags.N
        };

        var elementType = elementStr switch
        {
            "蓝" or "b" => ElementType.Blue,
            "红" or "r" => ElementType.Red,
            "绿" or "g" => ElementType.Green,
            "黄" or "y" => ElementType.Yellow,
            "光" or "l" => ElementType.Light,
            "暗" or "d" => ElementType.Dark,
            _ => ElementType.Blue
        };

        var qqNumber = (context.Message.Find(d => d is CqAtMsg) as CqAtMsg)?.Target ?? context.Sender.UserId;
        try
        {
            var bytes = await _imageUtil.BuildAvatar(qqNumber, characterRarity, lv, elementType);
            if (bytes.Length == 0)
            {
                return;
            }

            var cqImageMsg = CqImageMsg.FromBytes(bytes);
            await _sessionAccessor.Session.SendGroupMessageAsync(context.GroupId, new CqMessage(cqImageMsg));
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    [CqMessageMatch(@"/(抽卡|gacha)\s*(?<name>列表|list|UP|命运|des|白金|pla)(?<indexStr>\d)?", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    public async Task DrawCard(CqGroupMessagePostContext context, string name, string indexStr)
    {
        if (!IsGroupAllowed(context)) return;
        _logger.LogInformation($"{nameof(DrawCard)} {name}");

        if (name == "列表" || name == "list")
        {
            var msg = new StringBuilder();
            List<(string name, string character)> upList = [];
            List<string> denisty = [];
            foreach (var gachaCaseUiMb in _gachaGenerator.GetUpList())
            {
                var gachaName = TextResourceTable.Get(gachaCaseUiMb.NameKey);
                if (gachaCaseUiMb.PickUpCharacterId > 0)
                {
                    var characterName = CharacterTable.GetCharacterName(gachaCaseUiMb.PickUpCharacterId);
                    upList.Add((gachaName, characterName));
                    denisty.Add(characterName);
                }
            }

            for (var i = 0; i < upList.Count; i++) msg.AppendLine($"up{i + 1}: {upList[i].name} - {upList[i].character}");
            for (var i = 0; i < denisty.Count; i++) msg.AppendLine($"命运|des{i + 1}: {denisty[i]}");
            List<string> otherList = ["白金|pla"];
            foreach (var other in otherList) msg.AppendLine($"{other}");
            await _sessionAccessor.Session.SendGroupMessageAsync(context.GroupId, new CqMessage(msg.ToString()));
            return;
        }

        try
        {
            var (image, msg) = name.ToLower() switch
            {
                "up" when int.TryParse(indexStr, out var index1) && index1 > 0 => await _gachaGenerator.Generate(GachaType.PickUp, context.Sender.UserId, index1),
                "命运" or "des" when int.TryParse(indexStr, out var index2) && index2 > 0 => await _gachaGenerator.Generate(GachaType.Destiny, context.Sender.UserId, index2),
                "白金" or "pla" => await _gachaGenerator.Generate(GachaType.Platinum, context.Sender.UserId),
                _ => ([], "")
            };
            if (image.Length > 0)
            {
                var cqImageMsg = CqImageMsg.FromBytes(image);
                await _sessionAccessor.Session.SendGroupMessageAsync(context.GroupId, new CqMessage(cqImageMsg, new CqTextMsg(msg), new CqReplyMsg(context.MessageId)));
            }
        }
        catch (Exception e)
        {
            await _sessionAccessor.Session.SendGroupMessageAsync(context.GroupId, new CqMessage(e.Message));
        }
    }

    [CqMessageMatch(@"^/更新主数据$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    public async Task UpdateMasterData(CqGroupMessagePostContext context)
    {
        if (!IsGroupAllowed(context)) return;
        if (!IsSenderAdmin(context)) return;
        _logger.LogInformation($"{nameof(UpdateMasterData)}");
        var msg = "";
        if (await _networkManager.DownloadMasterCatalog())
        {
            _networkManager.SetCultureInfo(_networkManager.CultureInfo);
            msg = "更新完成";
        }
        else
        {
            msg = "暂无更新";
        }

        await _sessionAccessor.Session.SendGroupMessageAsync(context.GroupId, new CqMessage(msg));
    }

    private bool IsSenderAdmin(CqGroupMessagePostContext context)
    {
        return _botOptions.Value.AdminIds.Contains(context.Sender.UserId);
    }
}