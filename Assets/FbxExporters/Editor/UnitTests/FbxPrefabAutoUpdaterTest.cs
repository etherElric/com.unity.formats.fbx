using UnityEngine;
using UnityEditor;
using NUnit.Framework;
using System.IO;
using System.Collections.Generic;

namespace FbxExporters.UnitTests
{
    /// <summary>
    /// Test that the post-import prefab updater works properly,
    /// by triggering it to run.
    /// </summary>
    public class FbxPrefabAutoUpdaterTest : ExporterTestBase
    {
        GameObject m_fbx;
        string m_fbxPath;
        GameObject m_prefab;
        string m_prefabPath;

        [SetUp]
        public void Init ()
        {
            var capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            m_fbx = ExportSelection(capsule);
            m_fbxPath = AssetDatabase.GetAssetPath(m_fbx);

            // Instantiate the fbx and create a prefab from it.
            // Delete the object right away (don't even wait for term).
            var fbxInstance = PrefabUtility.InstantiatePrefab(m_fbx) as GameObject;
            new FbxPrefabAutoUpdater.FbxPrefabUtility(fbxInstance.AddComponent<FbxPrefab>()).SetSourceModel(m_fbx);
            m_prefabPath = GetRandomPrefabAssetPath();
            m_prefab = PrefabUtility.CreatePrefab(m_prefabPath, fbxInstance);
            AssetDatabase.Refresh ();
            Assert.AreEqual(m_prefabPath, AssetDatabase.GetAssetPath(m_prefab));
            GameObject.DestroyImmediate(fbxInstance);
        }

        [Test]
        public void BasicTest ()
        {
            var fbxPrefabPath = FbxPrefabAutoUpdater.FindFbxPrefabAssetPath();
            Assert.IsFalse(string.IsNullOrEmpty(fbxPrefabPath));
            Assert.IsTrue(fbxPrefabPath.EndsWith(FbxPrefabAutoUpdater.FBX_PREFAB_FILE));

            Assert.IsTrue(FbxPrefabAutoUpdater.IsFbxAsset("Assets/path/to/foo.fbx"));
            Assert.IsFalse(FbxPrefabAutoUpdater.IsFbxAsset("Assets/path/to/foo.png"));

            Assert.IsTrue(FbxPrefabAutoUpdater.IsPrefabAsset("Assets/path/to/foo.prefab"));
            Assert.IsFalse(FbxPrefabAutoUpdater.IsPrefabAsset("Assets/path/to/foo.fbx"));
            Assert.IsFalse(FbxPrefabAutoUpdater.IsPrefabAsset("Assets/path/to/foo.png"));

            var imported = new HashSet<string>( new string [] { "Assets/path/to/foo.fbx", m_fbxPath } );
            Assert.IsTrue(FbxPrefabAutoUpdater.MayHaveFbxPrefabToFbxAsset(m_prefabPath, fbxPrefabPath,
                        imported));
        }

        [Test]
        public void RectTransformTest ()
        {
            //Create a hierarchy with a RectTransform
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            capsule.AddComponent<RectTransform>();
            
            capsule.GetComponent<RectTransform>().localScale = new Vector3(1,2,3);
            capsule.GetComponent<RectTransform>().localPosition = new Vector3(100,200,300);

            capsule.transform.parent = cube.transform;

            GameObject originalFBX = ExportSelection(cube);

            //instantiate our hierarchy as a prefab
            var oldInstance = PrefabUtility.InstantiatePrefab(originalFBX) as GameObject;
            Assert.IsTrue(oldInstance);

            Debug.Log("ORIGINAL: " + oldInstance.transform.GetChild(0).GetComponent<RectTransform>().localScale);

            //Get the file path of our original hierarchy
            string filePath = AssetDatabase.GetAssetPath(originalFBX);

            //Create an "updated" hierarchy
            var cube2 = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var capsule2 = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            capsule2.AddComponent<RectTransform>();

            capsule2.GetComponent<RectTransform>().localScale = new Vector3(3, 2, 1);
            capsule2.GetComponent<RectTransform>().localPosition = new Vector3(300, 200, 100);

            capsule2.transform.parent = cube2.transform;

            GameObject newFBX = ExportSelection(cube2);            

            //export our updated hierarchy to the same file path as the original
            SleepForFileTimestamp();
            FbxExporters.Editor.ModelExporter.ExportObject(filePath, cube2);
            AssetDatabase.Refresh();

            var newInstance = PrefabUtility.InstantiatePrefab(newFBX) as GameObject;
            Assert.IsTrue(newInstance);

            Debug.Log("NEW BOI: " + newInstance.transform.GetChild(0).GetComponent<RectTransform>().localScale);

            AssertSameHierarchy(cube2, oldInstance, ignoreRootName: true, ignoreRootTransform: true);

        }

        [Test]
        public void ReplaceTest ()
        {
            // Instantiate the prefab.
            var oldInstance = PrefabUtility.InstantiatePrefab(m_prefab) as GameObject;
            Assert.IsTrue(oldInstance);

            // Create a new hierarchy. It's marked for delete already.
            var newHierarchy = CreateHierarchy();

            // Export it to the same fbx path. But first, wait one second so
            // that its timestamp differs enough for Unity to notice it
            // changed.
            SleepForFileTimestamp();
            FbxExporters.Editor.ModelExporter.ExportObject(m_fbxPath, newHierarchy);
            AssetDatabase.Refresh();

            // Verify that a new instance of the prefab got updated.
            var newInstance = PrefabUtility.InstantiatePrefab(m_prefab) as GameObject;
            Assert.IsTrue(newInstance);
            AssertSameHierarchy(newHierarchy, newInstance, ignoreRootName: true, ignoreRootTransform: true);

            // Verify that the old instance also got updated.
            AssertSameHierarchy(newHierarchy, oldInstance, ignoreRootName: true, ignoreRootTransform: true);
        }

    }
}

namespace FbxExporters.PerformanceTests {

    class FbxPrefabAutoUpdaterTestPerformance : FbxExporters.UnitTests.ExporterTestBase {
        [Test]
        public void ExpensivePerformanceTest ()
        {
            const int n = 200;
            const int NoUpdateTimeLimit = 500; // milliseconds
            const int OneUpdateTimeLimit = 500; // milliseconds

            var stopwatch = new System.Diagnostics.Stopwatch ();
            stopwatch.Start();

            // Create N fbx models and N/2 prefabs.
            // Each prefab points to an fbx model.
            //
            // Then modify one fbx model. Shouldn't take longer than 1s.
            var hierarchy = CreateGameObject("the_root");
            var baseName = GetRandomFbxFilePath();
            FbxExporters.Editor.ModelExporter.ExportObject(baseName, hierarchy);

            // Create N fbx models by copying files. Import them all at once.
            var names = new string[n];
            names[0] = baseName;
            stopwatch.Reset();
            stopwatch.Start();
            for(int i = 1; i < n; ++i) {
                names[i] = GetRandomFbxFilePath();
                System.IO.File.Copy(names[0], names[i]);
            }
            Debug.Log("Created fbx files in " + stopwatch.ElapsedMilliseconds);

            stopwatch.Reset();
            stopwatch.Start();
            AssetDatabase.Refresh();
            Debug.Log("Imported fbx files in " + stopwatch.ElapsedMilliseconds);

            // Create N/2 prefabs, each one depends on one of the fbx assets.
            // This loop is very slow, which is sad because it's not the point
            // of the test. That's the only reason we halve n.
            stopwatch.Reset();
            stopwatch.Start();
            var fbxFiles = new GameObject[n / 2];
            for(int i = 0; i < n / 2; ++i) {
                fbxFiles[i] = AssetDatabase.LoadMainAssetAtPath(names[i]) as GameObject;
                Assert.IsTrue(fbxFiles[i]);
            }
            Debug.Log("Loaded fbx files in " + stopwatch.ElapsedMilliseconds);

            stopwatch.Reset();
            stopwatch.Start();
            for(int i = 0; i < n / 2; ++i) {
                var instance = CreateGameObject("prefab_" + i);
                Assert.IsTrue(instance);
                var fbxPrefab = instance.AddComponent<FbxPrefab>();
                new FbxPrefabAutoUpdater.FbxPrefabUtility(fbxPrefab).SetSourceModel(fbxFiles[i]);
                PrefabUtility.CreatePrefab(GetRandomPrefabAssetPath(), fbxFiles[i]);
            }
            Debug.Log("Created prefabs in " + stopwatch.ElapsedMilliseconds);

            // Export a new hierarchy and update one fbx file.
            // Make sure we're timing just the assetdatabase refresh by
            // creating a file and then copying it, and not the FbxExporter.
            var newHierarchy = CreateHierarchy();
            var newHierarchyName = GetRandomFbxFilePath();
            FbxExporters.Editor.ModelExporter.ExportObject(newHierarchyName, newHierarchy);
            try {
                UnityEngine.Debug.unityLogger.logEnabled = false;
                stopwatch.Reset ();
                stopwatch.Start ();
                File.Copy(newHierarchyName, names[0], overwrite: true);
                AssetDatabase.Refresh(); // force the update right now.
            } finally {
                UnityEngine.Debug.unityLogger.logEnabled = true;
            }
            Debug.Log("Import (one change) in " + stopwatch.ElapsedMilliseconds);
            Assert.LessOrEqual(stopwatch.ElapsedMilliseconds, NoUpdateTimeLimit);

            // Try what happens when no prefab gets updated.
            try {
                UnityEngine.Debug.unityLogger.logEnabled = false;
                stopwatch.Reset ();
                stopwatch.Start ();
                string newHierarchyFbxFile = GetRandomFbxFilePath();
                File.Copy(names[0], newHierarchyFbxFile);
                AssetDatabase.Refresh(); // force the update right now.
            } finally {
                UnityEngine.Debug.unityLogger.logEnabled = true;
            }
            Debug.Log("Import (no changes) in " + stopwatch.ElapsedMilliseconds);
            Assert.LessOrEqual(stopwatch.ElapsedMilliseconds, OneUpdateTimeLimit);
        }
    }
}
