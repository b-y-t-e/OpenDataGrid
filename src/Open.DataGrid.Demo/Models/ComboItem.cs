namespace Open.DataGrid.Demo.Models
{
    public class ComboItem
    {
        public int Id { get; set; }
        public string Label { get; set; }

        public ComboItem(int id, string label) { Id = id; Label = label; }
        public override string ToString() => Label;
    }
}
