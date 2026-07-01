namespace SwingingPaint.Rope
{
    /// <summary>
    /// The rope kinds the project must support. Each maps to a physically
    /// meaningful preset in <see cref="RopeMaterial.CreatePreset"/>.
    /// </summary>
    public enum RopeType
    {
        Steel,      // كابل فولاذي
        Plastic,    // حبل بلاستيك
        Rubber,     // حبل مطاطي
        Cotton,     // حبل قطني
        Nylon,      // حبل نايلون
        Hemp,       // حبل قنب
        Elastic,    // حبل مطاطي (شديد المطاطية)
        Chain,      // سلسلة
        Custom      // مخصّص (تُضبط قيمه يدويًا)
    }
}
