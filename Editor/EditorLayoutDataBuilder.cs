using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;

namespace MarkupAttributes.Editor
{
    internal class InlineEditorData
    {
        public UnityEditor.Editor editor;
        public InlineEditorMode mode;
        public bool enabled = true;

        public InlineEditorData(UnityEditor.Editor editor, InlineEditorAttribute attribute)
        {
            this.editor = editor;
            mode = attribute.Mode;
        }
    }

    internal static class EditorLayoutDataBuilder
    {
        public static void BuildLayoutData(SerializedObject serializedObject,
            out SerializedProperty[] allProps,
            out SerializedProperty[] topLevelProps,
            out List<PropertyLayoutData> layoutData,
            out Dictionary<SerializedProperty, InlineEditorData> inlineEditors,
            out List<TargetObjectWrapper> targetsRequireUpdate,
            out Dictionary<string, object> managedReferenceTypesCache,
            out Dictionary<string, int> arraySizesCache)
        {
            var props = new List<SerializedProperty>();
            layoutData = new List<PropertyLayoutData>();
            inlineEditors = new Dictionary<SerializedProperty, InlineEditorData>();
            targetsRequireUpdate = new List<TargetObjectWrapper>();
            managedReferenceTypesCache = new Dictionary<string, object>();
            arraySizesCache = new Dictionary<string, int>();
            Type targetType = serializedObject.targetObject.GetType();

            topLevelProps = MarkupEditorUtils.GetSerializedObjectProperties(serializedObject);
            GetLayoutDataForSiblings(null, topLevelProps, targetType, 
                new TargetObjectWrapper(serializedObject.targetObject),
                props, layoutData, inlineEditors, targetsRequireUpdate, managedReferenceTypesCache, arraySizesCache);
            allProps = props.ToArray();
        }

        private static int GetLayoutDataForSiblings(InspectorLayoutGroup scopeGroup,
            SerializedProperty[] siblings, Type targetType, TargetObjectWrapper targetObjectWrapper,
            List<SerializedProperty> allProps, List<PropertyLayoutData> layoutData,
            Dictionary<SerializedProperty, InlineEditorData> inlineEditors, List<TargetObjectWrapper> targetObjectWrappers,
            Dictionary<string, object> managedReferenceTypesCache, Dictionary<string, int> arraySizesCache)
        {
            int scopesToClose = 0;
            for (int i = 0; i < siblings.Length; i++)
            {
                var sibling = siblings[i];
                var groups = new List<InspectorLayoutGroup>();
                if (scopeGroup != null && i == 0)
                {
                    groups.Add(scopeGroup);
                }

                FieldInfo fieldInfo = null;
                Type currentType = targetType;
                while (currentType != null)
                {
                    fieldInfo = currentType.GetField(sibling.name, MarkupEditorUtils.DefaultBindingFlags);
                    if (fieldInfo != null)
                        break;
                    currentType = currentType.BaseType;
                }

                PropertyLayoutData data = null;
                if (fieldInfo != null)
                {
                    // layout groups
                    var groupAttribues = fieldInfo.GetCustomAttributes<LayoutGroupAttribute>(true).ToArray();

                    bool isPropertyHidden = false;
                    foreach (var groupAttribute in groupAttribues)
                    {
                        var group = CreateGroupFromAttribute(ref isPropertyHidden, 
                            groupAttribute, sibling, targetObjectWrapper);
                        if (group != null)
                            groups.Add(group);
                    }

                    // conditionals 
                    var hideConditions = new List<ConditionWrapper>();
                    foreach (var attribute in fieldInfo.GetCustomAttributes<HideIfAttribute>())
                    {
                        var condition = ConditionWrapper.Create(attribute.Condition, targetObjectWrapper);
                        if (condition != null)
                            hideConditions.Add(condition);
                    }

                    var disableConditions = new List<ConditionWrapper>();
                    foreach (var attribute in fieldInfo.GetCustomAttributes<DisableIfAttribute>())
                    {
                        var condition = ConditionWrapper.Create(attribute.Condition, targetObjectWrapper);
                        if (condition != null)
                            disableConditions.Add(condition);
                    }

                    var end = fieldInfo.GetCustomAttribute<EndGroupAttribute>();
                    data = new PropertyLayoutData(groups, hideConditions, disableConditions, end, fieldInfo);
                    data.alwaysHide = isPropertyHidden;
                    data.isTopLevel = scopeGroup == null;
                    data.numberOfScopesToClose = scopesToClose;
                    scopesToClose = 0;
                    

                    // InlineEditors
                    var inline = fieldInfo.GetCustomAttribute<InlineEditorAttribute>();
                    if (inline != null)
                        inlineEditors.Add(sibling, new InlineEditorData(null, inline));
                }

                allProps.Add(sibling);
                layoutData.Add(data);

                if (sibling.propertyType == SerializedPropertyType.ManagedReference)
                {
                    managedReferenceTypesCache[sibling.propertyPath] = sibling.managedReferenceValue;
                }
                if (sibling.isArray && sibling.propertyType == SerializedPropertyType.Generic)
                {
                    arraySizesCache[sibling.propertyPath] = sibling.arraySize;
                }

                // Nested properties
                bool isManagedReference = sibling.propertyType == SerializedPropertyType.ManagedReference;
                bool isArray = sibling.isArray && sibling.propertyType == SerializedPropertyType.Generic;
                if ((sibling.propertyType == SerializedPropertyType.Generic || isManagedReference)
                    && fieldInfo != null)
                {
                    var markedUp = fieldInfo.GetCustomAttribute<MarkedUpTypeAttribute>();
                    
                    Type subTargetType = null;
                    object subTarget = null;
                    if (isManagedReference)
                    {
                        subTarget = sibling.managedReferenceValue;
                        if (subTarget != null)
                        {
                            subTargetType = subTarget.GetType();
                        }
                    }
                    else if (isArray)
                    {
                        // Array/List elements flattening
                        Type elementType = null;
                        if (fieldInfo.FieldType.IsArray)
                            elementType = fieldInfo.FieldType.GetElementType();
                        else if (fieldInfo.FieldType.IsGenericType && fieldInfo.FieldType.GetGenericTypeDefinition() == typeof(List<>))
                            elementType = fieldInfo.FieldType.GetGenericArguments()[0];

                        if (elementType != null)
                        {
                            if (markedUp == null)
                                markedUp = elementType.GetCustomAttribute<MarkedUpTypeAttribute>(true);

                            if (markedUp != null)
                            {
                                data.includeChildren = false;
                                
                                int size = sibling.arraySize;
                                for (int indexInArray = 0; indexInArray < size; indexInArray++)
                                {
                                    var elementProp = sibling.GetArrayElementAtIndex(indexInArray);
                                    
                                    Type elementTargetType = elementType;
                                    object elementTarget = null;
                                    if (elementProp.propertyType == SerializedPropertyType.ManagedReference)
                                    {
                                        elementTarget = elementProp.managedReferenceValue;
                                        if (elementTarget != null)
                                            elementTargetType = elementTarget.GetType();
                                    }
                                    else
                                    {
                                        elementTarget = MarkupEditorUtils.GetTargetObjectOfProperty(elementProp);
                                        if (elementTarget != null)
                                            elementTargetType = elementTarget.GetType();
                                    }

                                    var elementData = new PropertyLayoutData(new List<InspectorLayoutGroup>(), new List<ConditionWrapper>(), new List<ConditionWrapper>(), null, fieldInfo);
                                    elementData.isTopLevel = false;
                                    elementData.includeChildren = true;

                                    allProps.Add(elementProp);
                                    layoutData.Add(elementData);

                                    if (elementProp.propertyType == SerializedPropertyType.ManagedReference)
                                    {
                                        managedReferenceTypesCache[elementProp.propertyPath] = elementProp.managedReferenceValue;
                                    }

                                    var elementMarkedUp = elementType.GetCustomAttribute<MarkedUpTypeAttribute>(true);
                                    if (elementTarget != null)
                                    {
                                        var concreteMarkedUp = elementTargetType.GetCustomAttribute<MarkedUpTypeAttribute>(true);
                                        if (concreteMarkedUp != null)
                                            elementMarkedUp = concreteMarkedUp;
                                    }

                                    if (elementMarkedUp != null && elementTarget != null)
                                    {
                                        var children = MarkupEditorUtils.GetChildrenOfProperty(elementProp).ToArray();
                                        if (children != null && children.Length > 0)
                                        {
                                            elementData.includeChildren = false;
                                            elementData.alwaysHide |= !elementMarkedUp.ShowControl;
                                            var subScopeGroup = InspectorLayoutGroup.CreateScopeGroup(
                                                "./" + elementProp.name, elementProp, elementTargetType.FullName, 
                                                elementMarkedUp.ShowControl, elementMarkedUp.IndentChildren);
                                            
                                            var elementWrapper = new TargetObjectWrapper(elementTarget, elementProp);
                                            scopesToClose += GetLayoutDataForSiblings(
                                                subScopeGroup, children, elementTargetType, elementWrapper, 
                                                allProps, layoutData, inlineEditors, targetObjectWrappers,
                                                managedReferenceTypesCache, arraySizesCache);
                                            scopesToClose += 1;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        subTarget = MarkupEditorUtils.GetTargetObjectOfProperty(sibling);
                        if (subTarget != null)
                        {
                            subTargetType = subTarget.GetType();
                        }
                    }

                    if (!isArray && markedUp == null && subTargetType != null)
                        markedUp = subTargetType.GetCustomAttribute<MarkedUpTypeAttribute>(true);

                    if (!isArray && markedUp != null && subTargetType != null)
                    {
                        var subTargetWrapper = new TargetObjectWrapper(subTarget, sibling);
                        if (subTargetType.IsValueType)
                            targetObjectWrappers.Add(subTargetWrapper);
                        if (subTargetType != targetType)
                        {
                            var children = MarkupEditorUtils.GetChildrenOfProperty(sibling).ToArray();
                            if (children != null && children.Length > 0)
                            {
                                data.includeChildren = false;
                                data.alwaysHide |= !markedUp.ShowControl;
                                var subScopeGroup = InspectorLayoutGroup.CreateScopeGroup(
                                    "./" + sibling.name, sibling, subTargetType.FullName, 
                                    markedUp.ShowControl, markedUp.IndentChildren);
                                scopesToClose += GetLayoutDataForSiblings(
                                    subScopeGroup, children, subTargetType, subTargetWrapper, 
                                    allProps, layoutData, inlineEditors, targetObjectWrappers,
                                    managedReferenceTypesCache, arraySizesCache);
                                scopesToClose += 1;
                            }
                        }
                    }
                }
            }
            return scopesToClose;
        }

        private static InspectorLayoutGroup CreateGroupFromAttribute(ref bool isHidden,
            LayoutGroupAttribute attribute, SerializedProperty property, TargetObjectWrapper targetObjectWrapper)
        {
            ConditionWrapper conditionWrapper = null;
            if (attribute.HasCondition)
            {
                conditionWrapper = ConditionWrapper.Create(attribute.Condition, targetObjectWrapper);
                if (conditionWrapper == null)
                    return null;
            }

            TogglableValueWrapper togglableValueWrapper = null;
            if (attribute.Toggle)
            {
                togglableValueWrapper = TogglableValueWrapper.Create(property);
                if (togglableValueWrapper == null)
                    return null;
                isHidden = true;
            }

            return new InspectorLayoutGroup(attribute, conditionWrapper, togglableValueWrapper);
        }
    }
}


