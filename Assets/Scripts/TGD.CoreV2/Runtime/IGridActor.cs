namespace TGD.CoreV2
{
    /// κΡλ䵽ӲҪСϢ
    public interface IGridActor
    {
        string Id { get; }
        Hex Anchor { get; set; }
        Facing4 Facing { get; set; }
        FootprintShape Footprint { get; }
    }
}
