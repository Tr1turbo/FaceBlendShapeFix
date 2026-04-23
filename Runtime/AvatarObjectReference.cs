// Derived from Modular Avatar (https://github.com/bdunderscore/modular-avatar)
// Original work Copyright (c) 2022 bd_
// Distributed under the MIT License; see THIRD_PARTY_NOTICES.md

using System;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Triturbo.FaceBlendShapeFix.Runtime
{
    public static class AvatarHierarchyUtil
    {
        private static readonly string[] s_AvatarRootTypeNames =
        {
            "nadena.dev.ndmf.runtime.components.NDMFAvatarRoot, nadena.dev.ndmf.runtime",
            "VRC.SDK3.Avatars.Components.VRCAvatarDescriptor, VRC.SDK3A",
        };

        private static Type[] s_AvatarRootTypes;

        public static string RelativePath(GameObject root, GameObject child)
        {
            return RelativePath(
                root != null ? root.transform : null,
                child != null ? child.transform : null
            );
        }

        public static string RelativePath(Transform root, Transform child)
        {
            if (root == child)
            {
                return string.Empty;
            }

            List<string> pathSegments = new List<string>();
            while (child != root && child != null)
            {
                pathSegments.Add(child.gameObject.name);
                child = child.parent;
            }

            if (child == null && root != null)
            {
                return null;
            }

            pathSegments.Reverse();
            return string.Join("/", pathSegments);
        }

        public static string AvatarRootPath(GameObject child)
        {
            if (child == null)
            {
                return null;
            }

            Transform avatarRoot = FindAvatarInParents(child.transform);
            if (avatarRoot == null)
            {
                return null;
            }

            return RelativePath(avatarRoot.gameObject, child);
        }

        public static bool IsAvatarRoot(Transform target)
        {
            return target != null &&
                   TryGetAvatarRootComponent(target, out _) &&
                   GetAvatarRootInThisAndParents(target.parent) == null;
        }

        public static Transform FindAvatarInParents(Transform target)
        {
            Component avatarRoot = GetAvatarRootInThisAndParents(target);
            return avatarRoot != null ? avatarRoot.transform : null;
        }

        private static Component GetAvatarRootInThisAndParents(Transform target)
        {
            Component candidate = null;
            while (target != null)
            {
                if (TryGetAvatarRootComponent(target, out Component component))
                {
                    candidate = component;
                }

                target = target.parent;
            }

            return candidate;
        }

        private static bool TryGetAvatarRootComponent(Transform target, out Component component)
        {
            foreach (Type rootType in GetAvatarRootTypes())
            {
                if (target.TryGetComponent(rootType, out component))
                {
                    return true;
                }
            }

            component = null;
            return false;
        }

        private static Type[] GetAvatarRootTypes()
        {
            if (s_AvatarRootTypes != null)
            {
                return s_AvatarRootTypes;
            }

            List<Type> resolvedTypes = new List<Type>(s_AvatarRootTypeNames.Length);
            foreach (string typeName in s_AvatarRootTypeNames)
            {
                Type resolvedType = ResolveType(typeName);
                if (resolvedType != null)
                {
                    resolvedTypes.Add(resolvedType);
                }
            }

            s_AvatarRootTypes = resolvedTypes.ToArray();
            return s_AvatarRootTypes;
        }

        private static Type ResolveType(string typeName)
        {
            Type resolvedType = Type.GetType(typeName, false);
            if (resolvedType != null)
            {
                return resolvedType;
            }

            int assemblySeparatorIndex = typeName.IndexOf(',');
            string fullTypeName = assemblySeparatorIndex >= 0
                ? typeName.Substring(0, assemblySeparatorIndex)
                : typeName;

            foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                resolvedType = assembly.GetType(fullTypeName, false);
                if (resolvedType != null)
                {
                    return resolvedType;
                }
            }

            return null;
        }
    }

    [Serializable]
    public class AvatarObjectReference
    {
        private static long s_HierarchyChangedSequence = long.MinValue;

        public const string AvatarRoot = "$$$AVATAR_ROOT$$$";

        public string referencePath;

#if UNITY_EDITOR
        [InitializeOnLoadMethod]
        private static void Initialize()
        {
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
        }

        private static void OnHierarchyChanged()
        {
            s_HierarchyChangedSequence++;
        }

        public static GameObject Get(SerializedProperty property)
        {
            if (property?.serializedObject == null)
            {
                return null;
            }

            UnityEngine.Object rootObject = property.serializedObject.targetObject;
            Transform hostTransform =
                (rootObject as Component)?.transform ??
                (rootObject as GameObject)?.transform;

            Transform avatarRoot = AvatarHierarchyUtil.FindAvatarInParents(hostTransform);
            if (avatarRoot == null)
            {
                return null;
            }

            SerializedProperty referencePathProperty = property.FindPropertyRelative("referencePath");
            SerializedProperty targetObjectProperty = property.FindPropertyRelative("targetObject");
            string path = referencePathProperty != null ? referencePathProperty.stringValue : string.Empty;
            Component directTarget = targetObjectProperty != null
                ? targetObjectProperty.objectReferenceValue as Component
                : null;

            Transform resolvedTransform = ResolveReferenceTransform(avatarRoot, path);
            if (resolvedTransform != null || !string.IsNullOrEmpty(path))
            {
                return resolvedTransform != null ? resolvedTransform.gameObject : null;
            }

            if (IsValidTarget(directTarget, avatarRoot))
            {
                return directTarget.gameObject;
            }

            return null;
        }
#endif

        protected static long HierarchyChangedSequence => s_HierarchyChangedSequence;

        protected static Transform ResolveReferenceTransform(Transform avatarRoot, string path)
        {
            if (avatarRoot == null || string.IsNullOrEmpty(path))
            {
                return null;
            }

            return path == AvatarRoot
                ? avatarRoot
                : avatarRoot.Find(path);
        }

        protected static string GetReferencePath(GameObject target)
        {
            if (target == null)
            {
                return string.Empty;
            }

            if (AvatarHierarchyUtil.IsAvatarRoot(target.transform))
            {
                return AvatarRoot;
            }

            return AvatarHierarchyUtil.AvatarRootPath(target) ?? string.Empty;
        }

        protected static bool IsValidTarget(Component candidate, Transform avatarRoot)
        {
            return candidate != null &&
                   avatarRoot != null &&
                   (candidate.transform == avatarRoot || candidate.transform.IsChildOf(avatarRoot));
        }
    }

    [Serializable]
    public class AvatarObjectReference<T> : AvatarObjectReference where T : Component
    {
        [SerializeField] internal T targetObject;

        private long _cachedSequence = long.MinValue;
        private bool _hasCachedResult;
        private string _cachedPath;
        private T _cachedSourceTarget;
        private T _cachedResolvedTarget;

        public AvatarObjectReference()
        {
        }

        public AvatarObjectReference(T target)
        {
            Set(target);
        }

        public bool IsConfigured => !string.IsNullOrEmpty(referencePath) || targetObject != null;

        public T Get(Component container)
        {
            if (_hasCachedResult &&
                _cachedSequence == HierarchyChangedSequence &&
                _cachedPath == referencePath &&
                ReferenceEquals(_cachedSourceTarget, targetObject) //To prevent Uniy fake null
                )
            {
                return _cachedResolvedTarget;
            }

            _hasCachedResult = true;
            _cachedSequence = HierarchyChangedSequence;
            _cachedPath = referencePath;
            _cachedSourceTarget = targetObject;

            if (container == null)
            {
                _cachedResolvedTarget = null;
                return null;
            }

            Transform avatarRoot = AvatarHierarchyUtil.FindAvatarInParents(container.transform);
            if (!string.IsNullOrEmpty(referencePath))
            {
                Transform targetTransform = ResolveReferenceTransform(avatarRoot, referencePath);
                _cachedResolvedTarget = targetTransform != null
                    ? targetTransform.GetComponent<T>()
                    : null;
                return _cachedResolvedTarget;
            }

            if (avatarRoot == null)
            {
                _cachedResolvedTarget = targetObject;
                return _cachedResolvedTarget;
            }

            if (IsValidTarget(targetObject, avatarRoot))
            {
                _cachedResolvedTarget = targetObject;
                return _cachedResolvedTarget;
            }

            _cachedResolvedTarget = null;

            return _cachedResolvedTarget;
        }

        public void Set(T target)
        {
            referencePath = GetReferencePath(target != null ? target.gameObject : null);
            targetObject = target;
            _cachedResolvedTarget = target;
            _cachedSourceTarget = target;
            _cachedPath = referencePath;
            _cachedSequence = HierarchyChangedSequence;
            _hasCachedResult = true;
        }
    }
}
