﻿using System;

namespace OpenDreamShared.Dream {
    public struct ViewRange {
        public readonly int Width, Height;
        public bool IsSquare => (Width == Height);

        //View can be centered in both directions?
        public bool IsCenterable => (Width % 2 == 1) && (Height % 2 == 1);

        /// <summary>
        /// The distance this ViewRange covers in every direction if <see cref="IsSquare"/> and
        /// <see cref="IsCenterable"/> are true
        /// </summary>
        public int Range => (IsSquare && IsCenterable) ? (Width - 1) / 2 : 0;

        public ViewRange(int range) {
            // A square covering "range" cells in each direction
            Width = range * 2 + 1;
            Height = range * 2 + 1;
        }

        public ViewRange(string range) {
            string[] split = range.Split("x");

            if (split.Length != 2) throw new Exception($"Invalid view range string \"{range}\"");
            Width = int.Parse(split[0]);
            Height = int.Parse(split[1]);
        }

        public override string ToString() {
            return $"{Width}x{Height}";
        }
    }
}
