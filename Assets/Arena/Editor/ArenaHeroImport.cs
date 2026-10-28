// Import settings for hero art in Assets/Arena/Resources/Heroes/<Hero>/ (see HeroModel.cs).
using System.IO;
using UnityEditor;
using UnityEngine;

public class ArenaHeroImport : AssetPostprocessor
{
    const string Root = "Assets/Arena/Resources/Heroes";
    bool Ours => assetPath.Replace('\\', '/').StartsWith(Root);
    string FileName => Path.GetFileNameWithoutExtension(assetPath).ToLowerInvariant();

    void OnPreprocessModel()
    {
        if (!Ours) return;
        var mi = (ModelImporter)assetImporter;
        mi.materialImportMode = ModelImporterMaterialImportMode.None; // the material is made at runtime (team colours)
        if (FileName == "tail")
        {
            mi.animationType = ModelImporterAnimationType.None;
            mi.importAnimation = false;
            mi.preserveHierarchy = true;
            mi.bakeAxisConversion = true;
            return;
        }
        mi.animationType = ModelImporterAnimationType.Human;
        mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        mi.importAnimation = FileName != "model";
        mi.optimizeGameObjects = false;
    }

    void OnPreprocessAnimation()
    {
        if (!Ours || FileName == "tail" || FileName == "model") return;
        var mi = (ModelImporter)assetImporter;
        bool loop = FileName == "idle" || FileName == "run" || FileName == "stun";
        var clips = mi.defaultClipAnimations;
        foreach (var c in clips)
        {
            c.name = FileName; c.loopTime = loop; c.loopPose = loop;
            // the sim moves the hero; animations stay in place
            c.lockRootRotation = true; c.lockRootHeightY = true; c.lockRootPositionXZ = true;
            c.keepOriginalOrientation = true; c.keepOriginalPositionY = true; c.keepOriginalPositionXZ = true;
        }
        mi.clipAnimations = clips;
    }

    void OnPreprocessTexture()
    {
        if (!Ours) return;
        var ti = (TextureImporter)assetImporter;
        ti.isReadable = true; // team kits are recoloured from it at runtime
        ti.sRGBTexture = true;
        ti.maxTextureSize = 2048;
    }

    [MenuItem("Arena/Переимпортировать героев")]
    static void Reimport()
    {
        foreach (var guid in AssetDatabase.FindAssets("", new[] { Root }))
        {
            var p = AssetDatabase.GUIDToAssetPath(guid);
            if (!AssetDatabase.IsValidFolder(p)) AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate);
        }
        Debug.Log("[Arena] герои переимпортированы");
    }
}
