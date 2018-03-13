using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

public class EditorUtil
{

    [MenuItem("Assets/Find References In Project",true)]
    public static bool IsSelectionFile()
    {
        return Selection.activeObject != null;
    }

    #region Reference

    public const string searchfilter = "t:AnimatorController t:Prefab t:Scene t:ScriptableObject";
    public const string animationSearchFilter = "t:AnimationClip";
    public const string materialSearchFilter = "t:Material";

    [MenuItem("Assets/Find References In Project")]
    public static void SelectReferencesInProject()
    {
        SelectReferencesInProject(Selection.assetGUIDs);
    }

    public static void SelectReferencesInProject(string assetGUID)
    {
        SelectReferencesInProject(new string[] { assetGUID });
    }

    public static void SelectReferencesInProject(string[] assetGUIDs)
    {
        FindReferencesInProject(assetGUIDs, r =>
        {
            EditorApplication.ExecuteMenuItem("Window/Project");
            if (r.Count > 0)
            {
                FoldProject();
                Debug.Log("引用数：" + r.Count);
            }
            else
            {
                Debug.Log("没有引用");
            }
            List<UnityEngine.Object> passInObjs = new List<UnityEngine.Object>();
            foreach(string guid in assetGUIDs)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if(!string.IsNullOrEmpty(path))
                {
                    passInObjs.Add(AssetDatabase.LoadMainAssetAtPath(path));
                }
            }
            Selection.objects = r.Select(s => AssetDatabase.LoadMainAssetAtPath(s)).Concat(passInObjs).ToArray();
            onFindFinished = null;
            r.Clear();
            //EditorUtility.UnloadUnusedAssetsImmediate(false);
        });
    }


    public class References
    {
        public string path;
        public string[] assetGUIDs;
    }

    static int assetNum = 0;
    static volatile int dealNum = 0;
    static volatile List<string> references = new List<string>();
    static Action<List<string>> onFindFinished;
    static object lockObj = new object();

    static List<string> allUnityAssetPaths = new List<string>();
    static List<string> dealtPaths = new List<string>();

    public static List<string> GetAllUnityAssets()
    {
        string[] anims = Directory.GetFiles(Application.dataPath, "*.anim", SearchOption.AllDirectories);
        for (int i = 0; i < anims.Length;i++ )
        {
            anims[i] = anims[i].Replace(Application.dataPath, "Assets");
        }
        string[] materials = AssetDatabase.FindAssets(materialSearchFilter);
        materials = materials.Select(s => AssetDatabase.GUIDToAssetPath(s)).Where(s=>string.IsNullOrEmpty(Path.GetExtension(s))).ToArray();
        List<string> paths = AssetDatabase.FindAssets(searchfilter).Select(s => AssetDatabase.GUIDToAssetPath(s)).ToList();
        paths.AddRange(anims);
        return paths;
    }

    public static void FindReferencesInProject(string assetGUID, Action<List<string>> onFinished)
    {
        FindReferencesInProject(new string[] { assetGUID }, onFindFinished);
    }

    public static void FindReferencesInProject(string[] assetGUIDs,Action<List<string>> onFinished)
    {
        if(EditorSettings.serializationMode != SerializationMode.ForceText)
        {
            if(EditorUtility.DisplayDialog("", "如果你要使用该功能，必须设置Unity的默认序列化模式为ForceText", "Set SerializationMode to ForceText", "Cancel"))
            {
                EditorSettings.serializationMode = SerializationMode.ForceText;
            }
            return;
        }

        references.Clear();
        onFindFinished = onFinished;
        dealtPaths.Clear();
        allUnityAssetPaths = GetAllUnityAssets();
        assetNum = allUnityAssetPaths.Count;
        dealNum = 0;
        EditorApplication.update += Updata;

        foreach (string path in allUnityAssetPaths)
        {
            References r = new References
            {
                assetGUIDs = assetGUIDs,
                path = path,
            };
            ThreadPool.QueueUserWorkItem(FindReferences, r);
        }
    }

    static void FindReferences(object obj)
    {
        References r = obj as References;
        string file = File.ReadAllText(r.path);
        foreach (string guid in r.assetGUIDs)
        {
            //Regex.IsMatch(file, guid);
            //if (Regex.IsMatch(file, guid))
            if (file.Contains(guid))
            {
                lock (lockObj)
                {
                    if (!references.Contains(r.path))
                    {
                        references.Add(r.path);
                    }
                }
                break;
            }
        }
        file = null;
        lock(lockObj)
        {
            dealNum++;
            dealtPaths.Add(r.path);
        }
        
    }

    public static void Updata()
    {
        if (assetNum > 0)
        {
            if (assetNum != dealNum)
            {
                if(EditorUtility.DisplayCancelableProgressBar("Find References", string.Format("{0}/{1}", dealNum, assetNum), (float)dealNum / (float)assetNum))
                {
                    EditorUtility.ClearProgressBar();
                    assetNum = 0;
                    dealNum = 0;
                    EditorApplication.update -= Updata;
                    foreach(string p in allUnityAssetPaths)
                    {
                        if(!dealtPaths.Contains(p))
                        {
                            Debug.Log(p);
                        }
                    }
                    dealtPaths.Clear();
                    allUnityAssetPaths.Clear();
                }
            }
            else
            {
                EditorUtility.ClearProgressBar();
                assetNum = 0;
                dealNum = 0;
                if(onFindFinished != null)
                {
                    onFindFinished(references);
                }
                EditorApplication.update -= Updata;
            }
        }
        else
        {
            EditorUtility.ClearProgressBar();
            EditorApplication.update -= Updata;
        }
    }

    #endregion

    [MenuItem("Assets/Fold Project", true, 0)]
    public static bool IsOneColumn()
    {
        Type pwu = typeof(ProjectWindowUtil);
        MethodInfo getPBMeth = pwu.GetMethod("GetProjectBrowserIfExists", BindingFlags.NonPublic | BindingFlags.Static);
        object pb = getPBMeth.Invoke(null, null);
        FieldInfo treeViewInfo = pb.GetType().GetField("m_AssetTree", BindingFlags.Instance | BindingFlags.NonPublic);
        object treeView = treeViewInfo.GetValue(pb);
        return treeView == null ? false : true;
    }

    /// <summary>
    /// 收起整个项目视图，只在Project视图为单列模式时可用
    /// </summary>
    [MenuItem("Assets/Fold Project",false,0)]
    public static void FoldProject()
    {
        //EditorGUIUtility.PingObject(AssetDatabase.LoadMainAssetAtPath("Assets"));
        //Debug.Log(EditorPrefs.GetString("AndroidSdkRoot"));

        //foreach(int i in InternalEditorUtility.expandedProjectWindowItems)
        //{
        //    Debug.Log(i);
        //}
        
        Type pwu = typeof(ProjectWindowUtil);
        MethodInfo getPBMeth = pwu.GetMethod("GetProjectBrowserIfExists", BindingFlags.NonPublic | BindingFlags.Static);
        object pb = getPBMeth.Invoke(null,null);
        FieldInfo treeViewInfo = pb.GetType().GetField("m_AssetTree", BindingFlags.Instance | BindingFlags.NonPublic);
        object treeView = treeViewInfo.GetValue(pb);
        if(treeView == null)
        {
            Debug.LogWarning("收起整个项目视图功能只在Project视图为单列模式时可用");
            return;
        }
        PropertyInfo dataInfo = treeView.GetType().GetProperty("data");
        object data = dataInfo.GetValue(treeView,null);
        FieldInfo rootItemInfo = data.GetType().GetField("m_RootItem", BindingFlags.NonPublic | BindingFlags.Instance);
        object rootItem = rootItemInfo.GetValue(data);
        Debug.Log(rootItem);
        MethodInfo expendInfo = data.GetType().GetMethod("SetExpandedWithChildren");
        expendInfo.Invoke(data, new object[] { rootItem, false });
    }
}
