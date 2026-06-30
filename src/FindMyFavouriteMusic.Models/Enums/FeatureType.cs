namespace Larpx.PersonalTools.FindMyFavouriteMusic.Models.Enums;

/// <summary>
/// 特征类型
/// </summary>
public enum FeatureType
{
    /// <summary>声学特征（MFCC、频谱质心、色度）</summary>
    Acoustic,
    /// <summary>深度学习特征（VGGish 等）</summary>
    Deep
}
