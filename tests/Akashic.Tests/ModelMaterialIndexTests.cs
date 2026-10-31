using Akashic.Core.Services.Preview;
using Xunit;

namespace Akashic.Tests;

public class ModelMaterialIndexTests
{
    [Fact]
    public void ExternalObjectsMapFbxMaterialNamesToUnityMaterials()
    {
        var root = Directory.CreateTempSubdirectory("akashic-model-");
        try
        {
            Directory.CreateDirectory(Path.Combine(root.FullName, "Assets", "Mat"));
            var material = Path.Combine(root.FullName, "Assets", "Mat", "Shinano_body.mat");
            File.WriteAllText(material, "%YAML 1.1\n");
            File.WriteAllText(material + ".meta", "fileFormatVersion: 2\nguid: 698e1c0df0ac6d54e9121cfcb99c50a6\n");

            var model = Path.Combine(root.FullName, "Assets", "Shinano.fbx");
            File.WriteAllText(model, "fbx");
            File.WriteAllText(model + ".meta", """
                fileFormatVersion: 2
                guid: 21faec3b318252e4d8bf08c0fd7cb57a
                ModelImporter:
                  externalObjects:
                  - first:
                      type: UnityEngine:Material
                      assembly: UnityEngine.CoreModule
                      name: Shinano_body
                    second: {fileID: 2100000, guid: 698e1c0df0ac6d54e9121cfcb99c50a6, type: 2}
                """);

            var index = new UnityMetaIndex(root.FullName);

            Assert.Equal(material, index.ResolveModelMaterial(model, "Shinano_body"));
            Assert.Null(index.ResolveModelMaterial(model, "Missing"));
            Assert.Equal(material, index.FindMaterialByName("Shinano_body"));
            Assert.Equal(material, index.FindMaterialByName("shinano body"));
        }
        finally
        {
            root.Delete(true);
        }
    }
}
