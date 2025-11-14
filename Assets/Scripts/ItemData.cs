using System;

[Serializable]
public class ItemData : BaseData
{
    public int id;
    public string text;
    public string imageName;
    public string exeName;

    public ItemData(string[] strs) : base(strs)
    {
        var setupActions = new Action<string>[]
            {
                value => id         = int.Parse(value),
                value => text       = value,
                value => imageName  = value,
                value => exeName    = value
            };
        if (strs.Length != setupActions.Length)
        {
            throw new ArgumentException($"Data {typeof(ItemData).ToString()} trying to create is not valid");
        }

        for (int i = 0; i < strs.Length; i++)
        {
            setupActions[i](strs[i]);
        }
    }
}
