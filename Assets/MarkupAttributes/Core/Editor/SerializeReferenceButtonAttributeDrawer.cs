#if UNITY_EDITOR

using System;
using System.Reflection;
using UnityEditor; 
using UnityEngine;
using MarkupAttributes;

namespace MarkupAttributes.Editor
{
    [CustomPropertyDrawer(typeof(SerializeReferenceButtonAttribute))]
    public class SerializeReferenceButtonAttributeDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            bool includeChildren = GetIncludeChildren(property);
            return EditorGUI.GetPropertyHeight(property, label, includeChildren);
        }
     
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property); 
            
            var labelPosition = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            EditorGUI.LabelField(labelPosition, label);    
             
            var typeRestrictions = SerializedReferenceUIDefaultTypeRestrictions.GetAllBuiltInTypeRestrictions(fieldInfo);
            property.DrawSelectionButtonForManagedReference(position, typeRestrictions);
            
            bool includeChildren = GetIncludeChildren(property);
            EditorGUI.PropertyField(position, property, GUIContent.none, includeChildren);
            
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
                    if (MarkedUpEditor.ActiveEditor != null && MarkedUpEditor.ActiveEditor.IsPropertyFlattened(property))
                    {
                        includeChildren = false;
                    }
                }
            }
            return includeChildren;
        }
    }
}
#endif
