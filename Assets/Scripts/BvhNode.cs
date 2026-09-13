namespace CollisionCheck
{
    /// <summary>
    /// Flat (array-based, not pointer-based) BVH node so the whole tree lives in a single
    /// NativeArray and is safe to read from Burst jobs. Interior nodes point at two children
    /// by index; leaf nodes reference a contiguous run of triangles in the tree's reordered
    /// triangle-index buffer (see BvhTree.TriangleIndices).
    /// </summary>
    public struct BvhNode
    {
        public Aabb Bounds;

        // Interior node: index of the right child in the node array (left child is always
        // this node's index + 1, a standard trick for flat BVH layouts).
        // Leaf node: -1.
        public int RightChild;

        // Leaf node only: offset into BvhTree.TriangleIndices and how many triangles follow.
        public int TriangleStart;
        public int TriangleCount;

        public bool IsLeaf => RightChild < 0;
    }
}
