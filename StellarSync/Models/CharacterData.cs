using System;
using System.Collections.Generic;
using System.Numerics;

namespace StellarSync.Models
{
    public class CharacterData
    {
        public string Name { get; set; } = string.Empty;
        public string World { get; set; } = string.Empty;
        public Vector3 Position { get; set; }
        public float Rotation { get; set; }
        public int ModelId { get; set; }
        public byte[] Customize { get; set; } = new byte[26];
        public Dictionary<string, object> Equipment { get; set; } = new Dictionary<string, object>();
        
        // Mod integration data
public string GlamourerData { get; set; } = string.Empty;
public Dictionary<string, HashSet<string>> PenumbraData { get; set; } = new Dictionary<string, HashSet<string>>();
public string PenumbraMetaManipulations { get; set; } = string.Empty;
public Dictionary<string, object> PenumbraFileMetadata { get; set; } = new Dictionary<string, object>(); // File metadata for HTTP transfer
    }
}


