using System.Text;
using AutoCtor;
using EleCho.GoCqHttpSdk.Message;
using MementoMori.BotServer.Utils;
using MementoMori.Ortega.Share.Enums;
using static MementoMori.Ortega.Share.Masters;

namespace MementoMori.BotServer.MmmrGenerators;

public abstract class GeneratorBase<T>
{
    protected const string tableStyle = """
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
}

[InjectSingleton]
[AutoConstruct]
public partial class SkillGenerator : GeneratorBase<SkillGenerator>
{
    private readonly ImageUtil _imageUtil;

    public bool TryGenerate(long id, out byte[] image, out string err)
    {
        err = "";
        image = [];
        var characterMb = CharacterTable.GetById(id);
        if (characterMb == null)
        {
            err = "未查询到此角色";
            return false;
        }

        var msg = new StringBuilder(tableStyle);
        var chaName = CharacterTable.GetCharacterName(characterMb.Id);
        msg.AppendLine($"<h1>{chaName}的技能</h1><table>");
        foreach (var skillId in characterMb.ActiveSkillIds)
        {
            var skillMb = ActiveSkillTable.GetById(skillId);
            var name = TextResourceTable.Get(skillMb.NameKey);
            msg.AppendFormat($"<tr><th colspan=\"2\" width=\"150px\">{name} (冷却 {skillMb.SkillMaxCoolTime})</th></tr>");
            foreach (var skillInfo in skillMb.ActiveSkillInfos)
            {
                var description = TextResourceTable.Get(skillInfo.DescriptionKey);
                if (skillInfo.EquipmentRarityFlags != 0 || string.IsNullOrEmpty(description))
                {
                    continue;
                }

                msg.AppendFormat($"<tr><td width=\"50px\">{GetSkillDesc(skillInfo.OrderNumber, skillInfo.CharacterLevel, skillInfo.EquipmentRarityFlags)}</td><td>{description}</td></tr>");
            }
        }

        foreach (var skillId in characterMb.PassiveSkillIds)
        {
            var skillMb = PassiveSkillTable.GetById(skillId);
            var name = TextResourceTable.Get(skillMb.NameKey);
            msg.AppendFormat($"<tr><th colspan=\"2\" width=\"150px\">{name}</th></tr>");
            foreach (var skillInfo in skillMb.PassiveSkillInfos)
            {
                var description = TextResourceTable.Get(skillInfo.DescriptionKey);
                if (skillInfo.EquipmentRarityFlags != 0 || string.IsNullOrEmpty(description))
                {
                    continue;
                }

                msg.AppendFormat($"<tr><td>{GetSkillDesc(skillInfo.OrderNumber, skillInfo.CharacterLevel, skillInfo.EquipmentRarityFlags)}</td><td>{description}</td></tr>");
            }
        }

        var equipmentMbs = EquipmentTable.GetArray().Where(d => d.Category == EquipmentCategory.Exclusive && (d.RarityFlags & EquipmentRarityFlags.SSR) != 0 && d.EquipmentLv == 180).ToList();
        foreach (var equipmentMb in equipmentMbs)
        {
            if (EquipmentExclusiveEffectTable.GetById(equipmentMb.ExclusiveEffectId).CharacterId == id)
            {
                msg.AppendFormat($"<tr><th colspan=\"2\" width=\"150px\">专属装备</th></tr>");
                var descriptionMb = EquipmentExclusiveSkillDescriptionTable.GetById(equipmentMb.EquipmentExclusiveSkillDescriptionId);
                msg.AppendFormat(@$"<tr><td>Ex.1</td><td>{TextResourceTable.Get(descriptionMb.Description1Key)}</td></tr>");
                msg.AppendFormat(@$"<tr><td>Ex.2</td><td>{TextResourceTable.Get(descriptionMb.Description2Key)}</td></tr>");
                msg.AppendFormat(@$"<tr><td>Ex.3</td><td>{TextResourceTable.Get(descriptionMb.Description3Key)}</td></tr>");
                break;
            }
        }

        msg.AppendLine("</table>");

        image = _imageUtil.HtmlToImage(msg.ToString(), 600);
        return true;

        string GetSkillDesc(int orderNumber, long characterLevel, EquipmentRarityFlags equipmentRarityFlags)
        {
            if (equipmentRarityFlags == 0)
            {
                return $"Lv.{orderNumber}";
            }

            var lvl = equipmentRarityFlags switch
            {
                EquipmentRarityFlags.SSR => "Ex.1",
                EquipmentRarityFlags.UR => "Ex.2",
                EquipmentRarityFlags.LR => "Ex.3",
                _ => "未知"
            };
            return $"{lvl}";
        }
    }
}