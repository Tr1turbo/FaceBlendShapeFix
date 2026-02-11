using System;
using System.Collections.Generic;
using UnityEngine;

namespace Triturbo.FaceBlendShapeFix.Runtime
{
    [CreateAssetMenu(fileName = "BlendShapeCategoryDatabase", menuName = "ShapeBlend/Blend Shape Category Database")]
    public sealed class BlendShapeCategoryDatabase : ScriptableObject
    {
        [SerializeField]
        private List<BlendShapeCategory> m_Categories = new List<BlendShapeCategory>();
        public IReadOnlyList<BlendShapeCategory> Categories => m_Categories;

        public bool TryMatch(BlendShapeMatchContext context, out BlendShapeCategoryMatch match)
        {
            match = default;

            if (m_Categories == null || m_Categories.Count == 0)
            {
                return false;
            }

            BlendShapeCategoryMatch? bestMatch = null;
            int bestPriority = int.MinValue;

            foreach (var category in m_Categories)
            {
                if (category == null)
                {
                    continue;
                }

                if (!category.TryMatch(context, out var candidate))
                {
                    continue;
                }

                int categoryPriority = category.Priority;
                if (bestMatch.HasValue && categoryPriority < bestPriority)
                {
                    continue;
                }

                bestMatch = candidate;
                bestPriority = categoryPriority;
            }

            if (!bestMatch.HasValue)
            {
                return false;
            }

            match = bestMatch.Value;
            return true;
        }
    }

    [Serializable]
    public sealed class BlendShapeCategory
    {
        [SerializeField] private string m_CategoryName = "New Category";
        [SerializeField] private int m_Priority = 0;
        [SerializeField] private List<TargetShapeEntry> m_TargetShapeEntries = new List<TargetShapeEntry>();

        public string CategoryName => m_CategoryName;
        public int Priority => m_Priority;
        public IReadOnlyList<TargetShapeEntry> Entries => m_TargetShapeEntries;

        internal bool TryMatch(BlendShapeMatchContext context, out BlendShapeCategoryMatch match)
        {
            match = default;

            if (m_TargetShapeEntries == null || m_TargetShapeEntries.Count == 0)
            {
                return false;
            }

            foreach (var entry in m_TargetShapeEntries)
            {
                if (entry == null)
                {
                    continue;
                }

                if (!entry.TryMatch(context, out var matchedAlias))
                {
                    continue;
                }

                match = new BlendShapeCategoryMatch(this, entry, matchedAlias);
                return true;
            }

            return false;
        }
    }

    [Serializable]
    public sealed class TargetShapeEntry
    {
        [SerializeField] private string m_DisplayName = "New Target";
        [SerializeField] private ShapeType m_TargetShapeType = ShapeType.Others;
        [SerializeField] private bool m_UseGlobalDefinitions = true;
        [SerializeField] private bool m_CaseSensitive = false;
        [SerializeField] private List<string> m_Aliases = new List<string>();

        public string DisplayName => m_DisplayName;
        public ShapeType TargetShapeType => m_TargetShapeType;
        public bool UseGlobalDefinitions => m_UseGlobalDefinitions;
        public bool IsCaseSensitive => m_CaseSensitive;
        public IReadOnlyList<string> Aliases => m_Aliases;

        internal bool TryMatch(BlendShapeMatchContext context, out string matchedAlias)
        {
            matchedAlias = null;

            if (string.IsNullOrEmpty(context.BlendShapeName) || m_Aliases == null || m_Aliases.Count == 0)
            {
                return false;
            }

            StringComparison comparison = m_CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            
            
            if (string.Equals(context.BlendShapeName, m_DisplayName, comparison))
            {
                matchedAlias = m_DisplayName;
                return true;
            }
            
            foreach (var alias in m_Aliases)
            {
                if (string.IsNullOrEmpty(alias))
                {
                    continue;
                }

                if (string.Equals(context.BlendShapeName, alias, comparison))
                {
                    matchedAlias = alias;
                    return true;
                }
            }

            return false;
        }
    }

    public readonly struct BlendShapeCategoryMatch
    {
        public BlendShapeCategory Category { get; }
        public TargetShapeEntry Entry { get; }
        public string MatchedAlias { get; }

        public string CategoryName => Category?.CategoryName ?? string.Empty;
        public string EntryName => Entry?.DisplayName ?? string.Empty;
        public ShapeType TargetShapeType => Entry != null ? Entry.TargetShapeType : ShapeType.Others;
        public bool UseGlobalDefinitions => Entry != null ? Entry.UseGlobalDefinitions : true;
        public int Priority => Category?.Priority ?? 0;

        public BlendShapeCategoryMatch(BlendShapeCategory category, TargetShapeEntry entry, string matchedAlias)
        {
            Category = category;
            Entry = entry;
            MatchedAlias = matchedAlias ?? string.Empty;
        }
    }

    public readonly struct BlendShapeMatchContext
    {
        public string BlendShapeName { get; }
        public SkinnedMeshRenderer Renderer { get; }
        public Mesh Mesh { get; }
        public int BlendShapeIndex { get; }

        public BlendShapeMatchContext(string blendShapeName, SkinnedMeshRenderer renderer, Mesh mesh, int blendShapeIndex)
        {
            BlendShapeName = blendShapeName;
            Renderer = renderer;
            Mesh = mesh;
            BlendShapeIndex = blendShapeIndex;
        }
    }
}
