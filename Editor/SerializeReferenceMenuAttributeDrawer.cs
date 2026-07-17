#if UNITY_EDITOR

using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using MarkupAttributes;

namespace MarkupAttributes.Editor
{
    [CustomPropertyDrawer(typeof(SerializeReferenceMenuAttribute))]
    public class SerializeReferenceMenuAttributeDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            bool includeChildren = GetIncludeChildren(property);
            return EditorGUI.GetPropertyHeight(property, label, includeChildren);
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            
            var typeRestrictions = SerializedReferenceUIDefaultTypeRestrictions.GetAllBuiltInTypeRestrictions(fieldInfo);
            property.ShowContextMenuForManagedReferenceOnMouseMiddleButton(position, typeRestrictions);
            
            bool includeChildren = GetIncludeChildren(property);
            EditorGUI.PropertyField(position, property, includeChildren);
            EditorGUI.EndProperty();
        }

        private bool GetIncludeChildren(SerializedProperty property)
        {
            bool includeChildren = true;
            if (MarkupGUI.IsInsideMarkedUpEditor && 
                property.propertyType == SerializedPropertyType.ManagedReference && 
                property.managedReferenceValue != null)
            {
                var valueType = property.managedReferenceValue.GetType();
                if (valueType.GetCustomAttribute<MarkedUpTypeAttribute>(true) != null ||
                    fieldInfo.GetCustomAttribute<MarkedUpTypeAttribute>(true) != null)
                {
                    includeChildren = false;
                }
            }
            return includeChildren;
        }
    }
}
#endif
