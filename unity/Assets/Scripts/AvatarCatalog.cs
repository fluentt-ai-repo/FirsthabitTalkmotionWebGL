#if UNITY_WEBGL
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Firsthabit.WebGL
{
    [CreateAssetMenu(fileName = "AvatarCatalog", menuName = "Firsthabit/Avatar Catalog")]
    public class AvatarCatalog : ScriptableObject
    {
        [Serializable]
        public class AvatarEntry
        {
            public string id;
            public GameObject prefab;
        }

        [SerializeField] private AvatarEntry[] entries;

        /// <summary>
        /// Build a dictionary from entries for fast lookup at runtime.
        /// </summary>
        public Dictionary<string, GameObject> BuildPrefabMap()
        {
            var map = new Dictionary<string, GameObject>();
            if (entries == null) return map;

            foreach (var entry in entries)
            {
                if (entry != null && !string.IsNullOrEmpty(entry.id) && entry.prefab != null)
                {
                    map[entry.id] = entry.prefab;
                }
            }
            return map;
        }

        public int Count => entries?.Length ?? 0;
    }
}
#endif
