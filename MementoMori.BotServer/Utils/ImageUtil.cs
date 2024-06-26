﻿using System.Runtime.InteropServices;
using System.Text;
using AutoCtor;
using HtmlConverter.Configurations;
using HtmlConverter.Options;
using MementoMori.Ortega.Common;
using MementoMori.Ortega.Share;
using MementoMori.Ortega.Share.Data.Gacha;
using MementoMori.Ortega.Share.Data.Item;
using MementoMori.Ortega.Share.Enums;
using MementoMori.Ortega.Share.Master.Table;
using SkiaSharp;

namespace MementoMori.BotServer.Utils;

[InjectSingleton]
[AutoConstruct]
public partial class ImageUtil
{
    private readonly IHttpClientFactory _httpClientFactory;
    const string assetsUrl = "https://list.moonheart.dev/d/public/mmtm";

    public byte[] HtmlToImage(string html, int? width = 600)
    {
        return HtmlConverter.Core.HtmlConverter.ConvertHtmlToImage(new ImageConfiguration
        {
            Content = html,
            Quality = 90,
            Format = ImageFormat.Jpg,
            Width = width,
            MinimumFontSize = 24,
        });
    }

    private async Task<SKImage?> DownloadImage(string url, string filename, bool ignoreCache = false)
    {
        Directory.CreateDirectory("tempImg");

        if (!ignoreCache && File.Exists($"tempImg/{filename}")) return SKImage.FromEncodedData($"tempImg/{filename}");

        using var httpClient = _httpClientFactory.CreateClient();
        var response = await httpClient.GetAsync(url);
        if (!response.IsSuccessStatusCode) return null;

        var stream = await response.Content.ReadAsStreamAsync();
        var image = SKImage.FromEncodedData(stream);
        if (image == null) return null;

        await using var tempFileStream = File.Create($"tempImg/{filename}");
        image.Encode(SKEncodedImageFormat.Png, 100).SaveTo(tempFileStream);

        return image;
    }

    public async Task<byte[]> BuildAvatar(long qqNumber, CharacterRarityFlags rarityFlags, int lv, ElementType elementType)
    {
        var avatarUrl = $"https://q1.qlogo.cn/g?b=qq&nk={qqNumber}&s=640";
        var avatar = await DownloadImage(avatarUrl, $"{qqNumber}.png", false);
        var border = rarityFlags >= CharacterRarityFlags.LR
            ? await DownloadImage($"{assetsUrl}/AddressableLocalAssets/Atlas/frame_common_lr_slice.png", "frame_common_lr_slice.png")
            : await DownloadImage($"{assetsUrl}/AddressableLocalAssets/Atlas/frame_common_slice.png", "frame_common_slice.png");
        var element = await DownloadImage($"{assetsUrl}/AddressableLocalAssets/Atlas/icon_element_{(int) elementType}.png", $"icon_element_{(int) elementType}.png");
        var star1 = rarityFlags >= CharacterRarityFlags.LRPlus6
            ? await DownloadImage($"{assetsUrl}/AddressableLocalAssets/Atlas/icon_rarity_plus_star_2.png", "icon_rarity_plus_star_2.png")
            : rarityFlags >= CharacterRarityFlags.LRPlus
                ? await DownloadImage($"{assetsUrl}/AddressableLocalAssets/Atlas/icon_rarity_plus_star_1.png", "icon_rarity_plus_star_1.png")
                : null;
        var star2 = rarityFlags >= CharacterRarityFlags.LRPlus6
            ? await DownloadImage($"{assetsUrl}/AddressableLocalAssets/Atlas/icon_rarity_plus_star_1.png", "icon_rarity_plus_star_1.png")
            : null;
        var deco = rarityFlags switch
        {
            CharacterRarityFlags.RPlus => await DownloadImage($"{assetsUrl}/AddressableLocalAssets/Atlas/frame_decoration_rplus.png", "frame_decoration_rplus.png"),
            CharacterRarityFlags.SRPlus => await DownloadImage($"{assetsUrl}/AddressableLocalAssets/Atlas/frame_decoration_srplus.png", "frame_decoration_srplus.png"),
            CharacterRarityFlags.SSRPlus => await DownloadImage($"{assetsUrl}/AddressableLocalAssets/Atlas/frame_decoration_ssrplus.png", "frame_decoration_ssrplus.png"),
            CharacterRarityFlags.URPlus => await DownloadImage($"{assetsUrl}/AddressableLocalAssets/Atlas/frame_decoration_urplus.png", "frame_decoration_urplus.png"),
            _ => null
        };
        var starCount1 = rarityFlags switch
        {
            CharacterRarityFlags.LRPlus => 1,
            CharacterRarityFlags.LRPlus2 => 2,
            CharacterRarityFlags.LRPlus3 => 3,
            CharacterRarityFlags.LRPlus4 => 4,
            CharacterRarityFlags.LRPlus5 => 5,
            CharacterRarityFlags.LRPlus6 => 1,
            CharacterRarityFlags.LRPlus7 => 2,
            CharacterRarityFlags.LRPlus8 => 3,
            CharacterRarityFlags.LRPlus9 => 4,
            CharacterRarityFlags.LRPlus10 => 5,
            _ => 0,
        };
        var starCount2 = rarityFlags switch
        {
            CharacterRarityFlags.LRPlus6 => 4,
            CharacterRarityFlags.LRPlus7 => 3,
            CharacterRarityFlags.LRPlus8 => 2,
            CharacterRarityFlags.LRPlus9 => 1,
            _ => 0
        };

        if (avatar == null || border == null || element == null) throw new ArgumentException("Image not found");

        var borderColor = ClientConst.Icon.CharacterFrameColorDictionary[rarityFlags > CharacterRarityFlags.LR ? CharacterRarityFlags.LR : rarityFlags];
        var colorFilter = SKColorFilter.CreateColorMatrix(new float[]
        {
            borderColor.R / 255.0f, 0, 0, 0, 0,
            0, borderColor.G / 255.0f, 0, 0, 0,
            0, 0, borderColor.B / 255.0f, 0, 0,
            0, 0, 0, 1, 0
        });

        return BuildAvatar(avatar, border, colorFilter, element, star1, star2, deco, lv, starCount1, starCount2);
    }

    public static byte[] BuildAvatar(SKImage avatar, SKImage border, SKColorFilter borderFilter, SKImage element, SKImage? star1, SKImage? star2, SKImage? deco, int lv, int starCount1, int starCount2)
    {
        if (starCount1 < 0 || starCount2 < 0 || starCount1 + starCount2 > 5) throw new ArgumentException("Invalid star count");
        if (starCount1 > 0 && star1 == null || starCount2 > 0 && star2 == null) throw new ArgumentException("Star image is required when star count is greater than 0");

        using var skBitmap = new SKBitmap(144, 144);
        using var skCanvas = new SKCanvas(skBitmap);

        skCanvas.DrawImage(avatar, new SKRect(8, 8, 128, 128));

        DrawNinePatch(skCanvas, border, borderFilter, new SKRect(0, 0, 144, 144), 26, 26, 26, 26);

        skCanvas.DrawImage(element, new SKRect(8, 8, 40, 40));

        var skPaint = new SKPaint(new SKFont(SKTypeface.Default, 24)) {Color = SKColor.Parse("#FFFFFF"), Style = SKPaintStyle.StrokeAndFill, StrokeWidth = 1};
        var shadowColor = SKColor.Parse("#FF000000");
        skPaint.ImageFilter = SKImageFilter.CreateDropShadow(0, 0, 5, 5, shadowColor);
        skCanvas.DrawText($"lv{lv}".PadLeft(5, ' '), new SKPoint(70, 32), skPaint);

        if (deco != null) skCanvas.DrawImage(deco, new SKRect(104, 105, 138, 139));

        for (int i = 0; i < starCount1 + starCount2; i++)
        {
            var star = i < starCount1 ? star1 : star2;
            var starSize = 22;
            var left = 24;
            var bottom = 136;
            skCanvas.DrawImage(star, new SKRect(left + i * 18, bottom - starSize, left + starSize + i * 18, bottom));
        }

        skCanvas.Flush();
        using var ms = new MemoryStream();
        skBitmap.Encode(SKEncodedImageFormat.Png, 100).SaveTo(ms);
        return ms.ToArray();
    }

    public static void DrawNinePatch(SKCanvas canvas, SKImage borderImage, SKColorFilter colorFilter, SKRect destination, int left, int top, int right, int bottom)
    {
        var imageWidth = borderImage.Width;
        var imageHeight = borderImage.Height;
        using var skPaint = new SKPaint();
        skPaint.ColorFilter = colorFilter;
        // 绘制边框的四个角
        // 左上
        canvas.DrawImage(borderImage, new SKRect(0, 0, left, top), new SKRect(destination.Left, destination.Top, destination.Left + left, destination.Top + top), skPaint);
        // 右上
        canvas.DrawImage(borderImage, new SKRect(imageWidth - right, 0, imageWidth, top), new SKRect(destination.Right - right, destination.Top, destination.Right, destination.Top + top), skPaint);
        // 左下
        canvas.DrawImage(borderImage, new SKRect(0, imageHeight - bottom, left, imageHeight), new SKRect(destination.Left, destination.Bottom - bottom, destination.Left + left, destination.Bottom),
            skPaint);
        // 右下
        canvas.DrawImage(borderImage, new SKRect(imageWidth - right, imageHeight - bottom, imageWidth, imageHeight),
            new SKRect(destination.Right - right, destination.Bottom - bottom, destination.Right, destination.Bottom), skPaint);

        // 绘制边框的四条边
        // 上
        canvas.DrawImage(borderImage, new SKRect(left, 0, imageWidth - right, top), new SKRect(destination.Left + left, destination.Top, destination.Right - right, destination.Top + top), skPaint);
        // 下
        canvas.DrawImage(borderImage, new SKRect(left, imageHeight - bottom, imageWidth - right, imageHeight),
            new SKRect(destination.Left + left, destination.Bottom - bottom, destination.Right - right, destination.Bottom), skPaint);
        // 左
        canvas.DrawImage(borderImage, new SKRect(0, top, left, imageHeight - bottom), new SKRect(destination.Left, destination.Top + top, destination.Left + left, destination.Bottom - bottom),
            skPaint);
        // 右
        canvas.DrawImage(borderImage, new SKRect(imageWidth - right, top, imageWidth, imageHeight - bottom),
            new SKRect(destination.Right - right, destination.Top + top, destination.Right, destination.Bottom - bottom), skPaint);
    }

    public static byte[] BuildGachaResultItem(SKImage? item, SKImage? background, SKImage? border, SKColorFilter borderFilter, SKImage? element, long count, int? secondaryCount = null,
        SKImage? secondaryBackground = null, bool useNinePatch = true)
    {
        using var skBitmap = new SKBitmap(144, 144);
        using var skCanvas = new SKCanvas(skBitmap);

        skCanvas.DrawImage(background, new SKRect(0, 0, 144, 144));
        skCanvas.DrawImage(item, new SKRect(8, 8, 136, 136));
        if (secondaryBackground != null) skCanvas.DrawImage(secondaryBackground, new SKRect(6, 6, 70, 70));
        if (useNinePatch)
        {
            DrawNinePatch(skCanvas, border, borderFilter, new SKRect(0, 0, 144, 144), 26, 26, 26, 26);
        }
        else
        {
            skCanvas.DrawImage(border, new SKRect(0, 0, 144, 144));
        }
        if (element != null) skCanvas.DrawImage(element, new SKRect(8, 8, 40, 40));

        var skPaint = new SKPaint(new SKFont(SKTypeface.Default, 20)) {Color = SKColor.Parse("#FFFFFF"), Style = SKPaintStyle.StrokeAndFill, StrokeWidth = 1};
        var shadowColor = SKColor.Parse("#FF000000");
        skPaint.ImageFilter = SKImageFilter.CreateDropShadow(0, 0, 5, 5, shadowColor);
        skCanvas.DrawText($"{count}".PadLeft(5, ' '), new SKPoint(90, 122), skPaint);
        if (secondaryCount != null) skCanvas.DrawText($"{secondaryCount}h", new SKPoint(18, 35), skPaint);

        skCanvas.Flush();
        using var ms = new MemoryStream();
        skBitmap.Encode(SKEncodedImageFormat.Png, 100).SaveTo(ms);
        return ms.ToArray();
    }

    public async Task<SKImage?> GetItemImage(IUserItem userItem)
    {
        switch (userItem.ItemType)
        {
            case ItemType.Character:
                return await DownloadImage($"{assetsUrl}/AddressableConvertAssets/CharacterIcon/CHR_{userItem.ItemId:000000}/CHR_{userItem.ItemId:000000}_00_s.png",
                    $"CHR_{userItem.ItemId:000000}_00_s.png");
            case ItemType.CurrencyFree:
            case ItemType.EquipmentReinforcementItem:
            case ItemType.QuestQuickTicket:
            case ItemType.MatchlessSacredTreasureExpItem:
            case ItemType.BossChallengeTicket:
            case ItemType.TowerBattleTicket:
            case ItemType.ExchangePlaceItem:
                var iconId1 = Masters.ItemTable.GetByItemTypeAndItemId(userItem.ItemType, userItem.ItemId).IconId;
                return await DownloadImage($"{assetsUrl}/AddressableConvertAssets/Icon/Item/Item_{iconId1:0000}.png", $"Item_{iconId1:0000}.png");
            case ItemType.TreasureChest:
                var iconId2 = Masters.TreasureChestTable.GetById(userItem.ItemId).IconId;
                return await DownloadImage($"{assetsUrl}/AddressableConvertAssets/Icon/Item/Item_{iconId2:0000}.png", $"Item_{iconId2:0000}.png");
            default:
                return null;
        }
    }

    public async Task<byte[]> GenerateGachaResultImage(List<GachaResultItem> result)
    {
        // backgound image: 1652x1080
        var gachaBackground = await DownloadImage($"{assetsUrl}/AddressableLocalAssets/Prefabs/Gacha/Background_GachaResult.png", "Background_GachaResult.png");
        var logo = await DownloadImage($"{assetsUrl}/AddressableLocalAssets/UI/TitleLogo/image_title_logo_Black_jaJP.png", "image_title_logo_Black_jaJP.png");

        var images = new List<SKImage>();
        foreach (var item in result)
        {
            var itemImage = await GetItemImage(item);
            SKImage? element = null;
            if (item.ItemType == ItemType.Character)
            {
                var elementType = Masters.CharacterTable.GetById(item.ItemId).ElementType;
                element = await DownloadImage($"{assetsUrl}/AddressableLocalAssets/Atlas/icon_element_{(int) elementType}.png", $"icon_element_{(int) elementType}.png");
            }

            var gachaBorder = item.CharacterRarityFlags == CharacterRarityFlags.None
                ? await DownloadImage($"{assetsUrl}/AddressableLocalAssets/Atlas/frame_common_watercolor.png", "frame_common_watercolor.png")
                : await DownloadImage($"{assetsUrl}/AddressableLocalAssets/Atlas/frame_common_slice.png", "frame_common_slice.png");
            var borderColor = ClientConst.Icon.CharacterFrameColorDictionary[item.CharacterRarityFlags > CharacterRarityFlags.LR ? CharacterRarityFlags.LR : item.CharacterRarityFlags];
            var borderFilter = SKColorFilter.CreateColorMatrix([
                borderColor.R / 255.0f, 0, 0, 0, 0,
                0, borderColor.G / 255.0f, 0, 0, 0,
                0, 0, borderColor.B / 255.0f, 0, 0,
                0, 0, 0, 1, 0
            ]);
            var itemBackground = await DownloadImage($"{assetsUrl}/AddressableLocalAssets/Atlas/plate_character.png", "plate_character.png");
            int? secondaryCount = item.ItemType == ItemType.QuestQuickTicket ? Masters.ItemTable.GetByItemTypeAndItemId(item.ItemType, item.ItemId).SecondaryFrameNum : null;
            var secondaryBackground = item.ItemType == ItemType.QuestQuickTicket ? await DownloadImage($"{assetsUrl}/AddressableLocalAssets/Atlas/base_number_06.png", $"base_number_06.png") : null;
            var resultItem = BuildGachaResultItem(itemImage, itemBackground, gachaBorder, borderFilter, element, item.ItemCount, secondaryCount, secondaryBackground, item.CharacterRarityFlags != CharacterRarityFlags.None);
            images.Add(SKImage.FromEncodedData(resultItem));
        }

        using var skBitmap = new SKBitmap(1652, 780);
        using var skCanvas = new SKCanvas(skBitmap);
        skCanvas.DrawImage(gachaBackground, new SKRect(0, 150, 1652, 780), new SKRect(0, 0, 1652, 780));
        for (var i = 0; i < images.Count; i++)
        {
            var x = 300 + i % 5 * 230;
            var y = 150 + i / 5 * 230;
            skCanvas.DrawImage(images[i], new SKRect(x, y, x + 144, y + 144));
        }

        // logo 在左下角, 420x180
        skCanvas.DrawImage(logo, new SKRect(100, 550, 520, 730));

        skCanvas.Flush();
        using var ms = new MemoryStream();
        skBitmap.Encode(SKEncodedImageFormat.Png, 100).SaveTo(ms);
        return ms.ToArray();
    }
}