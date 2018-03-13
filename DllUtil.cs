using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public class DllUtil : EditorWindow
{

    Vector2 scrollPos;

    Object dll;
    string dllPath;
    Dictionary<Object,Object> dllScripts = new Dictionary<Object,Object>();


    struct IDTRans
    {
        public string keyID;
        public string valueID;
        public string keyGuid;
        public string valueGuid;
    }

    List<IDTRans> ids = new List<IDTRans>();
    List<bool> replaceDone = new List<bool>();
    object l = new object();


    bool autoRefresh = false;

     
    [MenuItem("Window/DllUtil")]
	static void Init()
    {
        GetWindow<DllUtil>().Show();
    }

    void Update()
    {
//        lock (replaceDone)
        {
            int n = replaceDone.Count;

            if (n != 0)
            {
                int done = replaceDone.Count(b => b == true);
                //Debug.Log(done);
                if (done != n)
                {
                    EditorUtility.DisplayCancelableProgressBar("DllReplace", string.Format("{0}/{1}", done, n), (float)done / (float)n);
                }
                else
                {
                    EditorPrefs.SetBool("kAutoRefresh", autoRefresh);
                    EditorUtility.ClearProgressBar();
                    replaceDone.Clear();
                    AssetDatabase.Refresh();
                }
            }
        }
    }

    void OnGUI()
    {
        EditorGUILayout.BeginHorizontal();

        EditorGUILayout.BeginVertical(GUILayout.Width(250f));

        Object obj = EditorGUILayout.ObjectField(dll, typeof(UnityEngine.Object), false);
        if(obj != dll)
        {
            if(obj == null)
            {
                dll = null;
                dllPath = "";
                return;
            }
            Debug.Log(obj.GetType());
            dllPath = AssetDatabase.GetAssetPath(obj);
            if(!dllPath.EndsWith(".dll"))
            {
                ShowNotification(new GUIContent("Not a dll"));
                dllPath = AssetDatabase.GetAssetPath(dll);
                return;
            } 
            dllScripts.Clear();
            dll = AssetDatabase.LoadMainAssetAtPath(dllPath);
            GetAllDllMonoScripts(dllPath);
            GetSameNativeMonoScripts();
        }
        if(dll == null)
        {
            return;
        }

        if (GUILayout.Button("Refresh"))
        {
            GetAllDllMonoScripts(dllPath);
            GetSameNativeMonoScripts();
        }

        if(dllScripts != null && dllScripts.Count > 0)
        {
            if (EditorSettings.serializationMode == SerializationMode.ForceText)
            {
                if (GUILayout.Button("Replace dll Scripts with Native"))
                {
                    ReplaceDllScriptWithNative();
                }
                if (GUILayout.Button("Replace Native Scripts with dll"))
                {
                    ReplaceNativeScriptWithDll();
                }
                
            }
            else
            {
                GUILayout.Label("使用该工具前必须将Unity默认序列化模式设置为强制为文本");
                if (GUILayout.Button("Set Asset Serialization to Force Text"))
                {
                    EditorSettings.serializationMode = SerializationMode.ForceText;
                }
            }
        }

        EditorGUILayout.EndVertical();

        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);


        foreach(var script in dllScripts)
        {
            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.ObjectField(script.Key,typeof(Object),false,GUILayout.Width(200f));
            if(GUILayout.Button("ref",GUILayout.Width(30f)))
            {
                EditorUtil.SelectReferencesInProject(Unsupported.GetLocalIdentifierInFile(script.Key.GetInstanceID()).ToString());
            }
            if(script.Key != script.Value)
            {
                EditorGUILayout.LabelField("<==>", GUILayout.Width(50f));
                EditorGUILayout.ObjectField(script.Value, typeof(Object), false, GUILayout.Width(200f));
                if (GUILayout.Button("ref", GUILayout.Width(30f)))
                {
                    EditorUtil.SelectReferencesInProject(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(script.Value)));
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndScrollView();

        EditorGUILayout.EndHorizontal();
    }

    void GetAllDllMonoScripts(string dllPath)
    {
        dllScripts = AssetDatabase.LoadAllAssetsAtPath(dllPath).ToDictionary(a => a);
    }

    void GetSameNativeMonoScripts()
    {
        Dictionary<Object, Object> tmp = new Dictionary<Object, Object>();
        foreach (var script in dllScripts)
        {
            string[] guids = AssetDatabase.FindAssets(script.Key.name + " t:Script");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);

                if (path.EndsWith("/" + script.Key.name + ".cs", true, System.Globalization.CultureInfo.CurrentCulture))
                {
                    tmp.Add(script.Key, AssetDatabase.LoadMainAssetAtPath(path));
                    break;
                }
            }
        }
        foreach (var t in tmp)
        {
            dllScripts[t.Key] = t.Value;
        }
    }

    void ReplaceDllScriptWithNative()
    {
        ids.Clear();

        foreach (var s in dllScripts)
        {
            if (s.Key == s.Value)
            {
                continue;
            }
            ids.Add(new IDTRans
            {
                valueID = Unsupported.GetLocalIdentifierInFile(s.Key.GetInstanceID()).ToString(),
                valueGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(s.Key)),
                keyID = Unsupported.GetLocalIdentifierInFile(s.Value.GetInstanceID()).ToString(),
                keyGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(s.Value))
            });
//            Debug.Log("keyGUID:" + ids[ids.Count - 1].keyGuid + "\tvaluGUID:" + ids[ids.Count - 1].valueGuid);
        }
        autoRefresh = EditorPrefs.GetBool("kAutoRefresh");
        EditorPrefs.SetBool("kAutoRefresh", false);
        EditorUtil.FindReferencesInProject(new string[]{AssetDatabase.AssetPathToGUID(dllPath)}, paths =>
        {
            replaceDone.Clear();
            replaceDone = paths.Select(s => false).ToList();
            Debug.Log(replaceDone.Count);

            foreach (var path in paths)
            {
                ThreadPool.QueueUserWorkItem(ReplaceProcess, path);
            }
        });
    }


    void ReplaceNativeScriptWithDll()
    {
        ids.Clear();
        
        foreach(var s in dllScripts)
        {
            if(s.Key == s.Value)
            {
                continue;
            }
            ids.Add(new IDTRans
            {
                keyID = Unsupported.GetLocalIdentifierInFile(s.Key.GetInstanceID()).ToString(),
                keyGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(s.Key)),
                valueID = Unsupported.GetLocalIdentifierInFile(s.Value.GetInstanceID()).ToString(),
                valueGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(s.Value))
            });
            
        }
        autoRefresh = EditorPrefs.GetBool("kAutoRefresh");
        EditorPrefs.SetBool("kAutoRefresh", false);
        EditorUtil.FindReferencesInProject(dllScripts.Select(s => AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(s.Value))).ToArray(), paths =>
        {
            replaceDone.Clear();
            replaceDone = paths.Select(s => false).ToList();
            Debug.Log(replaceDone.Count);

            foreach (var path in paths)
            {
                ThreadPool.QueueUserWorkItem(ReplaceProcess, path);
            }
        });
    }

    void ReplaceProcess(object obj)
    {
        
        string path = obj.ToString();
        string[] lines = File.ReadAllLines(path);
        for (int i = 0; i < lines.Length; i++)
        {
            for(int j = 0;j<ids.Count;j++)
            {
                if (lines[i].Contains(ids[j].valueGuid) && lines[i].Contains(ids[j].valueID))
                {
                    lines[i] = lines[i].Replace(ids[j].valueID, ids[j].keyID);
                    lines[i] = lines[i].Replace(ids[j].valueGuid, ids[j].keyGuid);
                }
            }
        }
        File.WriteAllLines(path, lines);
        lock (replaceDone)
        {
            for(int i = 0;i<replaceDone.Count;i++)
            {
                if(replaceDone[i] == false)
                {
                    replaceDone[i] = true;
                    break;
                }
            }
        }
        //Debug.Log(path);
    }

    void OnSelectionChange()
    {
        Debug.Log(Unsupported.GetLocalIdentifierInFile(Selection.activeInstanceID));
    }

}
