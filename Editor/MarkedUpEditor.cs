using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MarkupAttributes.Editor
{
    [CanEditMultipleObjects]
    public class MarkedUpEditor : UnityEditor.Editor
    {
        private SerializedProperty[] allProps;
        private SerializedProperty[] firstLevelProps;
        private List<PropertyLayoutData> layoutData;

        private InspectorLayoutController layoutController;
        private CallbackManager callbackManager;
        private Dictionary<SerializedProperty, InlineEditorData> inlineEditors;
        private List<TargetObjectWrapper> targetsRequireUpdate;
        private Dictionary<string, string> managedReferenceTypesCache;

        protected virtual void OnInitialize() { }
        protected virtual void OnCleanup() { }
        protected void AddCallback(SerializedProperty property, CallbackEvent type, Action<SerializedProperty> callback)
        {
            callbackManager.AddCallback(property, type, callback);
        }

        protected void OnEnable()
        {
            InitializeMarkedUpEditor();
        }

        protected void OnDisable()
        {
            CleanupMarkedUpEditor();
        }

        public override void OnInspectorGUI()
        {
            DrawMarkedUpInspector();
        }

        protected void InitializeMarkedUpEditor()
        {
            RebuildLayoutData();
            OnInitialize();
        }

        protected void CleanupMarkedUpEditor()
        {
            OnCleanup();
            if (inlineEditors != null)
            {
                foreach (var item in inlineEditors)
                {
                    if (item.Value != null && item.Value.editor != null)
                    {
                        DestroyImmediate(item.Value.editor);
                    }
                }
            }
        }

        private void RebuildLayoutData()
        {
            if (inlineEditors != null)
            {
                foreach (var item in inlineEditors)
                {
                    if (item.Value != null && item.Value.editor != null)
                    {
                        DestroyImmediate(item.Value.editor);
                    }
                }
            }

            EditorLayoutDataBuilder.BuildLayoutData(serializedObject, out allProps, 
                out firstLevelProps, out layoutData, out inlineEditors, out targetsRequireUpdate, out managedReferenceTypesCache);
            layoutController = new InspectorLayoutController(target.GetType().FullName,
                layoutData.ToArray());
            callbackManager = new CallbackManager(firstLevelProps);
        }

        private bool CheckManagedReferenceTypesChanged()
        {
            if (managedReferenceTypesCache == null)
                return false;
            foreach (var kvp in managedReferenceTypesCache)
            {
                var prop = serializedObject.FindProperty(kvp.Key);
                if (prop == null)
                    return true;
                if (prop.managedReferenceFullTypename != kvp.Value)
                    return true;
            }
            return false;
        }

        protected bool DrawMarkedUpInspector()
        {
            var previousIsInside = MarkupGUI.IsInsideMarkedUpEditor;
            MarkupGUI.IsInsideMarkedUpEditor = true;
            try
            {
                serializedObject.UpdateIfRequiredOrScript();
                if (CheckManagedReferenceTypesChanged())
                {
                    RebuildLayoutData();
                }

                EditorGUI.BeginChangeCheck();

                CreateInlineEditors();
                UpdateTargets();
                int topLevelIndex = 1;
                layoutController.Begin();

                if (!MarkupGUI.IsInsideInlineEditor)
                {
                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUILayout.PropertyField(allProps[0]);
                    }
                }

                for (int i = 1; i < allProps.Length; i++)
                {
                    layoutController.BeforeProperty(i);
                    if (layoutController.ScopeVisible)
                    {
                        using (new EditorGUI.DisabledScope(!layoutController.ScopeEnabled))
                        {
                            DrawProperty(i, topLevelIndex);
                        }
                    }

                    if (layoutController.IsTopLevel(i))
                        topLevelIndex += 1;
                }
                layoutController.Finish();

                serializedObject.ApplyModifiedProperties();
                return EditorGUI.EndChangeCheck();
            }
            finally
            {
                MarkupGUI.IsInsideMarkedUpEditor = previousIsInside;
            }
        }

        private void DrawProperty(int index, int topLevelIndex)
        {
            var prop = allProps[index];
            bool topLevel = layoutController.IsTopLevel(index);

            if (topLevel) callbackManager.InvokeCallback(topLevelIndex, CallbackEvent.BeforeProperty);
            

            using (new EditorGUI.DisabledScope(!layoutController.IsPropertyEnabled(index)))
            {
                if (layoutController.IsPropertyVisible(index))
                {
                    if (!topLevel || !callbackManager.InvokeCallback(index, CallbackEvent.ReplaceProperty))
                    {
                        if (inlineEditors.ContainsKey(prop))
                        {
                            InlineEditorData data = inlineEditors[prop];
                            MarkupGUI.DrawEditorInline(prop, data.editor, data.mode, data.enabled);
                        }
                        else
                        {
                            EditorGUILayout.PropertyField(prop, layoutController.IncludeChildren(index));
                        }
                    }
                }
            }
            if (topLevel) callbackManager.InvokeCallback(topLevelIndex, CallbackEvent.AfterProperty);
        }

        private void CreateInlineEditors()
        {
            var props = new List<SerializedProperty>(inlineEditors.Keys);
            foreach (var prop in props)
            {
                var editor = inlineEditors[prop].editor;

                if (prop.objectReferenceValue != serializedObject.targetObject)
                {
                    Material material = prop.objectReferenceValue as Material;
                    if (material != null)
                    {
                        CreateCachedEditor(material, typeof(HeaderlessMaterialEditor), ref editor);
                        inlineEditors[prop].enabled = AssetDatabase.GetAssetPath(material).StartsWith("Assets");
                    }
                    else
                        CreateCachedEditor(prop.objectReferenceValue, null, ref editor);
                }
                else
                {
                    editor = null;
                    prop.objectReferenceValue = null;
                    Debug.LogError("Self reference in the InlinedEditor property is not allowed.");
                }

                
                inlineEditors[prop].editor = editor;
            }
        }

        private void UpdateTargets()
        {
            foreach (var wrapper in targetsRequireUpdate)
            {
                wrapper.Update();
            }
        }
    }
}
