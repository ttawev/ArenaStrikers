// Import settings for hero art in Assets/Arena/Resources/Heroes/<Hero>/ (see HeroModel.cs).
using System.IO;
using UnityEditor;
using UnityEngine;

public class ArenaHeroImport : AssetPostprocessor
{
    const string Root = "Assets/Arena/Resources/Heroes";
    const string RefModel = "Assets/Arena/AnimRef/ybot.fbx"; // Mixamo Y Bot in T-pose, WITH skin: the reference pose for every clip
    bool Ours => assetPath.Replace('\\', '/').StartsWith(Root);
    bool IsRef => assetPath.Replace('\\', '/') == RefModel;
    bool IsClip => assetPath.Replace('\\', '/').StartsWith(Root + "/Anim/");
    string FileName => Path.GetFileNameWithoutExtension(assetPath).ToLowerInvariant();

    void OnPreprocessModel()
    {
        if (IsRef)
        {
            var r = (ModelImporter)assetImporter;
            r.animationType = ModelImporterAnimationType.Human;
            r.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            r.importAnimation = false; r.materialImportMode = ModelImporterMaterialImportMode.None;
            return;
        }
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
        // a Blender-built model keeps its named flat-colour materials (Kit, Skin, Glow…), see HeroModel.NamedMat
        if (FileName == "model") mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
        mi.animationType = ModelImporterAnimationType.Human;
        mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        // An animation-only Mixamo file has no T-pose: its skeleton rests in the first frame of the move.
        // An avatar built from that pose skews every clip (limp, twisted feet) — so clips borrow Y Bot's T-pose avatar.
        if (IsClip)
        {
            var refAvatar = System.Linq.Enumerable.FirstOrDefault(System.Linq.Enumerable.OfType<Avatar>(AssetDatabase.LoadAllAssetsAtPath(RefModel)));
            if (refAvatar != null) { mi.avatarSetup = ModelImporterAvatarSetup.CopyFromOther; mi.sourceAvatar = refAvatar; }
            else Debug.LogWarning("[Arena] нет " + RefModel + " — анимации импортированы со своей позой (возможна хромота)");
        }
        mi.importAnimation = FileName != "model";
        mi.optimizeGameObjects = false;
    }

    void OnPreprocessAnimation()
    {
        if (!Ours || FileName == "tail" || FileName == "model") return;
        var mi = (ModelImporter)assetImporter;
        // looping clips: idles, run, dizzy stun, dribble and goal celebrations (cel_*)
        bool loop = FileName == "idle" || FileName == "run" || FileName == "stun" || FileName == "dribble" || FileName == "run_ball" || FileName == "run_back" || FileName.StartsWith("strafe_") || FileName == "idle_show" || FileName.StartsWith("cel_");
        var clips = mi.defaultClipAnimations;
        foreach (var c in clips)
        {
            c.name = FileName; c.loopTime = loop; c.loopPose = loop;
            // the sim moves the hero; animations stay in place
            c.lockRootRotation = true; c.lockRootHeightY = true; c.lockRootPositionXZ = true;
            c.keepOriginalOrientation = true; c.keepOriginalPositionY = true; c.keepOriginalPositionXZ = true;
        }
        if (FileName == "run" && clips.Length > 0)
        {   // a mirrored copy: HeroModel plays both half a cycle apart, so the run is perfectly symmetric (no limp)
            var src = clips[0];
            var m = new ModelImporterClipAnimation
            {
                name = "run_mirror", takeName = src.takeName, firstFrame = src.firstFrame, lastFrame = src.lastFrame,
                loopTime = true, loopPose = true, mirror = true,
                lockRootRotation = true, lockRootHeightY = true, lockRootPositionXZ = true,
                keepOriginalOrientation = true, keepOriginalPositionY = true, keepOriginalPositionXZ = true,
            };
            clips = new[] { src, m };
        }
        mi.clipAnimations = clips;
    }

    void OnPreprocessTexture()
    {
        if (!Ours) return;
        var ti = (TextureImporter)assetImporter;
        ti.maxTextureSize = 2048;
        if (FileName == "normal") { ti.textureType = TextureImporterType.NormalMap; ti.isReadable = false; return; }
        ti.isReadable = true; // team kits are recoloured from it at runtime
        ti.sRGBTexture = true;
        ti.maxTextureSize = 2048;
    }

    [MenuItem("Arena/Переимпортировать героев")]
    static void Reimport()
    {
        if (File.Exists(RefModel)) AssetDatabase.ImportAsset(RefModel, ImportAssetOptions.ForceUpdate); // the reference avatar first
        foreach (var guid in AssetDatabase.FindAssets("", new[] { Root }))
        {
            var p = AssetDatabase.GUIDToAssetPath(guid);
            if (!AssetDatabase.IsValidFolder(p)) AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate);
        }
        Debug.Log("[Arena] герои переимпортированы");
    }
}
