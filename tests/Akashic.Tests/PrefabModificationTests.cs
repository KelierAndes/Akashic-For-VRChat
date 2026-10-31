using Akashic.Core.Services.Preview;
using Xunit;

namespace Akashic.Tests;

public class PrefabModificationTests
{
    [Fact]
    public void NestedModificationBlockIsAppliedToTheSourcePrefab()
    {
        var root = Directory.CreateTempSubdirectory("akashic-prefab-");
        try
        {
            const string sourceGuid = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
            var source = Path.Combine(root.FullName, "Base.prefab");
            File.WriteAllText(source, """
                %YAML 1.1
                %TAG !u! tag:unity3d.com,2011:
                --- !u!1 &100
                GameObject:
                  m_Component:
                  - component: {fileID: 200}
                  m_Name: Root
                  m_IsActive: 1
                --- !u!4 &200
                Transform:
                  m_GameObject: {fileID: 100}
                  m_LocalPosition: {x: 0, y: 0, z: 0}
                  m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}
                  m_LocalScale: {x: 1, y: 1, z: 1}
                  m_Children: []
                  m_Father: {fileID: 0}
                """);
            File.WriteAllText(source + ".meta", "fileFormatVersion: 2\nguid: " + sourceGuid + "\n");

            var variant = Path.Combine(root.FullName, "Variant.prefab");
            File.WriteAllText(variant, """
                %YAML 1.1
                %TAG !u! tag:unity3d.com,2011:
                --- !u!1001 &500
                PrefabInstance:
                  m_Modification:
                    m_TransformParent: {fileID: 0}
                    m_Modifications:
                    - target: {fileID: 200, guid: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa, type: 3}
                      propertyPath: m_LocalPosition.x
                      value: 1.5
                      objectReference: {fileID: 0}
                    - target: {fileID: 100, guid: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa, type: 3}
                      propertyPath: m_Name
                      value: Renamed
                      objectReference: {fileID: 0}
                  m_SourcePrefab: {fileID: 100100000, guid: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa, type: 3}
                """);
            File.WriteAllText(variant + ".meta", "fileFormatVersion: 2\nguid: bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\n");

            var scene = new PrefabParser(root.FullName, new UnityMetaIndex(root.FullName))
                .Parse("Variant.prefab");

            var node = Assert.Single(scene.Nodes);
            Assert.Equal("Renamed", node.Name);
            Assert.Equal(1.5f, node.Position[0], 3);
        }
        finally
        {
            root.Delete(true);
        }
    }
}
