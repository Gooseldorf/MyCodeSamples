using Unity.Mathematics;

namespace ECSTest.Structs
{
    public struct CellData 
    {
        public Cell Cell;
        public int2 Position;

        public CellData(Cell cell, int2 position)
        {
            Cell = cell;
            Position = position;
        }

        private bool Equals(CellData other) => Cell.Equals(other.Cell) && Position.Equals(other.Position);

        public override bool Equals(object obj) => obj is CellData other && Equals(other);
    }
}