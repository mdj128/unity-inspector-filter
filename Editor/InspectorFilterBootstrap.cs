using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Budorf.UnityInspectorFilter.Editor
{
    [InitializeOnLoad]
    internal static class InspectorFilterBootstrap
    {
        private const string HostName = "budorf-inspector-filter-host";
        private const string SearchFieldName = "budorf-inspector-filter-search";
        private static readonly Type InspectorWindowType = Type.GetType("UnityEditor.InspectorWindow, UnityEditor");

        static InspectorFilterBootstrap()
        {
            EditorApplication.update += EnsureHostsExist;
        }

        private static void EnsureHostsExist()
        {
            if (InspectorWindowType == null)
            {
                return;
            }

            var inspectors = Resources.FindObjectsOfTypeAll(InspectorWindowType);
            foreach (var inspectorObject in inspectors)
            {
                if (inspectorObject is not EditorWindow inspectorWindow)
                {
                    continue;
                }

                var root = inspectorWindow.rootVisualElement;
                if (root == null)
                {
                    continue;
                }

                var existing = root.Q<InspectorFilterHost>(HostName);
                var targetContainer = ResolveTargetContainer(root);
                if (targetContainer == null)
                {
                    continue;
                }

                if (existing != null)
                {
                    if (existing.parent != targetContainer)
                    {
                        existing.RemoveFromHierarchy();
                        targetContainer.Insert(0, existing);
                    }

                    existing.RefreshIfNeeded();
                    continue;
                }

                var host = new InspectorFilterHost
                {
                    name = HostName
                };

                targetContainer.Insert(0, host);
            }
        }

        private static VisualElement ResolveTargetContainer(VisualElement root)
        {
            var scrollView = root.Q<ScrollView>();
            if (scrollView?.contentContainer != null)
            {
                return scrollView.contentContainer;
            }

            return root;
        }

        private sealed class InspectorFilterHost : VisualElement
        {
            private readonly TextField _searchField;
            private readonly Label _statusLabel;
            private readonly ScrollView _resultsScrollView;
            private int _selectionHash;
            private string _lastQuery = string.Empty;

            public InspectorFilterHost()
            {
                style.flexShrink = 0;
                style.marginBottom = 6;
                style.paddingLeft = 6;
                style.paddingRight = 6;
                style.paddingTop = 6;
                style.paddingBottom = 4;
                style.borderBottomWidth = 1;
                style.borderBottomColor = new Color(0f, 0f, 0f, 0.15f);
                style.backgroundColor = new Color(0f, 0f, 0f, 0.04f);

                _searchField = new TextField("Inspector Filter")
                {
                    name = SearchFieldName
                };
                _searchField.RegisterValueChangedCallback(_ => RebuildResults());

                _statusLabel = new Label("Type to search the current selection.")
                {
                    style =
                    {
                        unityFontStyleAndWeight = FontStyle.Italic,
                        marginTop = 4,
                        color = new Color(1f, 1f, 1f, 0.7f)
                    }
                };

                _resultsScrollView = new ScrollView
                {
                    style =
                    {
                        maxHeight = 280,
                        marginTop = 4,
                        display = DisplayStyle.None
                    }
                };

                Add(_searchField);
                Add(_statusLabel);
                Add(_resultsScrollView);
            }

            public void RefreshIfNeeded()
            {
                var currentHash = GetSelectionHash();
                var currentQuery = _searchField.value ?? string.Empty;
                if (currentHash == _selectionHash && string.Equals(currentQuery, _lastQuery, StringComparison.Ordinal))
                {
                    return;
                }

                RebuildResults();
            }

            private void RebuildResults()
            {
                _selectionHash = GetSelectionHash();
                _lastQuery = _searchField.value ?? string.Empty;
                _resultsScrollView.Clear();

                var query = _lastQuery.Trim();
                if (string.IsNullOrEmpty(query))
                {
                    _statusLabel.text = "Type to search the current selection.";
                    _resultsScrollView.style.display = DisplayStyle.None;
                    return;
                }

                var targets = BuildTargets();
                if (targets.Count == 0)
                {
                    _statusLabel.text = "No searchable selection.";
                    _resultsScrollView.style.display = DisplayStyle.None;
                    return;
                }

                var groups = new List<PropertyMatchGroup>();
                foreach (var target in targets)
                {
                    var matches = CollectMatches(target.Target, query);
                    if (matches.Count == 0)
                    {
                        continue;
                    }

                    groups.Add(new PropertyMatchGroup(target.Target, target.GroupLabel, matches));
                }

                if (groups.Count == 0)
                {
                    _statusLabel.text = $"No matches for \"{query}\".";
                    _resultsScrollView.style.display = DisplayStyle.None;
                    return;
                }

                _statusLabel.text = $"{groups.Sum(group => group.Matches.Count)} matches in {groups.Count} script/component group(s).";
                _resultsScrollView.style.display = DisplayStyle.Flex;

                foreach (var group in groups)
                {
                    _resultsScrollView.Add(BuildGroupUi(group));
                }
            }

            private VisualElement BuildGroupUi(PropertyMatchGroup group)
            {
                var groupContainer = new Foldout
                {
                    text = $"{group.GroupLabel} ({group.Matches.Count})",
                    value = true
                };
                groupContainer.style.marginTop = 4;

                var serializedObject = new SerializedObject(group.Target);
                foreach (var match in group.Matches)
                {
                    var property = serializedObject.FindProperty(match.PropertyPath);
                    if (property == null)
                    {
                        continue;
                    }

                    var row = new VisualElement();
                    row.style.marginTop = 4;
                    row.style.paddingTop = 3;
                    row.style.paddingBottom = 3;
                    row.style.paddingLeft = 6;
                    row.style.paddingRight = 6;
                    row.style.borderTopWidth = 1;
                    row.style.borderTopColor = new Color(1f, 1f, 1f, 0.06f);

                    if (!string.IsNullOrEmpty(match.SectionLabel))
                    {
                        var sectionLabel = new Label(match.SectionLabel);
                        sectionLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                        sectionLabel.style.fontSize = 11;
                        sectionLabel.style.color = new Color(1f, 1f, 1f, 0.78f);
                        row.Add(sectionLabel);
                    }

                    var pathLabel = new Label(match.ReadablePath);
                    pathLabel.style.fontSize = 10;
                    pathLabel.style.color = new Color(1f, 1f, 1f, 0.55f);
                    row.Add(pathLabel);

                    var field = new PropertyField(property.Copy(), property.displayName);
                    field.Bind(serializedObject);
                    row.Add(field);

                    groupContainer.Add(row);
                }

                return groupContainer;
            }

            private static int GetSelectionHash()
            {
                unchecked
                {
                    var hash = 17;
                    foreach (var instanceId in Selection.instanceIDs)
                    {
                        hash = (hash * 31) + instanceId;
                    }

                    return hash;
                }
            }

            private static List<SelectionTarget> BuildTargets()
            {
                var activeObject = Selection.activeObject;
                if (activeObject == null)
                {
                    return new List<SelectionTarget>();
                }

                if (activeObject is GameObject gameObject)
                {
                    return gameObject
                        .GetComponents<Component>()
                        .Where(component => component != null)
                        .Select(component => new SelectionTarget(component, component.GetType().Name))
                        .ToList();
                }

                return new List<SelectionTarget>
                {
                    new(activeObject, activeObject.GetType().Name)
                };
            }

            private static List<PropertyMatch> CollectMatches(UnityEngine.Object target, string query)
            {
                var matches = new List<PropertyMatch>();
                var serializedObject = new SerializedObject(target);
                var iterator = serializedObject.GetIterator();
                var normalizedQuery = query.Trim();
                var enterChildren = true;

                while (iterator.NextVisible(enterChildren))
                {
                    enterChildren = false;

                    var property = iterator.Copy();
                    var readablePath = BuildReadablePath(property);
                    var sectionLabel = ResolveSectionLabel(target.GetType(), property);
                    if (!IsMatch(property.displayName, readablePath, sectionLabel, target.GetType().Name, normalizedQuery))
                    {
                        continue;
                    }

                    matches.Add(new PropertyMatch(property.propertyPath, readablePath, sectionLabel));
                }

                return matches;
            }

            private static bool IsMatch(string displayName, string readablePath, string sectionLabel, string scriptName, string query)
            {
                return Contains(displayName, query)
                    || Contains(readablePath, query)
                    || Contains(sectionLabel, query)
                    || Contains(scriptName, query);
            }

            private static bool Contains(string source, string query)
            {
                return !string.IsNullOrEmpty(source)
                    && source.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
            }

            private static string BuildReadablePath(SerializedProperty property)
            {
                var rawPath = property.propertyPath
                    .Replace(".Array.data[", "[", StringComparison.Ordinal)
                    .Replace(".managedReference<", "<", StringComparison.Ordinal);

                var segments = rawPath.Split('.');
                var nicified = segments
                    .Select(segment =>
                    {
                        var bracketIndex = segment.IndexOf('[');
                        if (bracketIndex >= 0)
                        {
                            var head = segment.Substring(0, bracketIndex);
                            var tail = segment.Substring(bracketIndex);
                            return $"{ObjectNames.NicifyVariableName(head)}{tail}";
                        }

                        return ObjectNames.NicifyVariableName(segment);
                    });

                return string.Join(" / ", nicified);
            }

            private static string ResolveSectionLabel(Type targetType, SerializedProperty property)
            {
                var header = TryGetHeaderLabel(targetType, property.propertyPath);
                if (!string.IsNullOrEmpty(header))
                {
                    return header;
                }

                var parentSegments = BuildParentSegments(property.propertyPath);
                if (parentSegments.Count == 0)
                {
                    return string.Empty;
                }

                return string.Join(" / ", parentSegments.Select(ObjectNames.NicifyVariableName));
            }

            private static string TryGetHeaderLabel(Type rootType, string propertyPath)
            {
                var field = TryResolveField(rootType, propertyPath);
                return field?.GetCustomAttribute<HeaderAttribute>()?.header;
            }

            private static FieldInfo TryResolveField(Type rootType, string propertyPath)
            {
                var currentType = rootType;
                FieldInfo resolvedField = null;

                foreach (var token in TokenizePropertyPath(propertyPath))
                {
                    if (token.IsArrayElement)
                    {
                        currentType = GetElementType(currentType);
                        continue;
                    }

                    resolvedField = GetFieldInHierarchy(currentType, token.Name);
                    if (resolvedField == null)
                    {
                        return null;
                    }

                    currentType = resolvedField.FieldType;
                }

                return resolvedField;
            }

            private static List<string> BuildParentSegments(string propertyPath)
            {
                var tokens = TokenizePropertyPath(propertyPath)
                    .Where(token => !token.IsArrayElement)
                    .Select(token => token.Name)
                    .ToList();

                if (tokens.Count <= 1)
                {
                    return new List<string>();
                }

                tokens.RemoveAt(tokens.Count - 1);
                return tokens;
            }

            private static List<PropertyPathToken> TokenizePropertyPath(string propertyPath)
            {
                var tokens = new List<PropertyPathToken>();
                var segments = propertyPath.Split('.');
                foreach (var segment in segments)
                {
                    if (segment == "Array")
                    {
                        continue;
                    }

                    if (segment.StartsWith("data[", StringComparison.Ordinal))
                    {
                        tokens.Add(PropertyPathToken.ArrayElement);
                        continue;
                    }

                    tokens.Add(new PropertyPathToken(segment, false));
                }

                return tokens;
            }

            private static Type GetElementType(Type type)
            {
                if (type.IsArray)
                {
                    return type.GetElementType();
                }

                if (type.IsGenericType)
                {
                    var genericArguments = type.GetGenericArguments();
                    if (genericArguments.Length == 1)
                    {
                        return genericArguments[0];
                    }
                }

                return type;
            }

            private static FieldInfo GetFieldInHierarchy(Type type, string fieldName)
            {
                while (type != null)
                {
                    var field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (field != null)
                    {
                        return field;
                    }

                    type = type.BaseType;
                }

                return null;
            }
        }

        private struct SelectionTarget
        {
            public readonly UnityEngine.Object Target;
            public readonly string GroupLabel;

            public SelectionTarget(UnityEngine.Object target, string groupLabel)
            {
                Target = target;
                GroupLabel = groupLabel;
            }
        }

        private sealed class PropertyMatchGroup
        {
            public readonly UnityEngine.Object Target;
            public readonly string GroupLabel;
            public readonly List<PropertyMatch> Matches;

            public PropertyMatchGroup(UnityEngine.Object target, string groupLabel, List<PropertyMatch> matches)
            {
                Target = target;
                GroupLabel = groupLabel;
                Matches = matches;
            }
        }

        private struct PropertyMatch
        {
            public readonly string PropertyPath;
            public readonly string ReadablePath;
            public readonly string SectionLabel;

            public PropertyMatch(string propertyPath, string readablePath, string sectionLabel)
            {
                PropertyPath = propertyPath;
                ReadablePath = readablePath;
                SectionLabel = sectionLabel;
            }
        }

        private struct PropertyPathToken
        {
            public static readonly PropertyPathToken ArrayElement = new PropertyPathToken(string.Empty, true);

            public readonly string Name;
            public readonly bool IsArrayElement;

            public PropertyPathToken(string name, bool isArrayElement)
            {
                Name = name;
                IsArrayElement = isArrayElement;
            }
        }
    }
}
