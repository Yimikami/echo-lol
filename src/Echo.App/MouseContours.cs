namespace Echo.App;

/// <summary>Normalized mouse silhouette shared by layout thumbnails and playback/export artwork.</summary>
internal static class MouseContours
{
    internal const string Shell = "M .5,.015 C .29,.015 .145,.075 .105,.225 C .065,.36 .055,.48 .055,.60 C .055,.86 .19,.985 .5,.985 C .81,.985 .945,.86 .945,.60 C .945,.48 .935,.36 .895,.225 C .855,.075 .71,.015 .5,.015 Z";
    internal const string Left = "M .48,.018 C .28,.018 .145,.08 .105,.225 C .077,.32 .06,.41 .058,.465 L .397,.465 L .397,.215 C .397,.16 .425,.14 .48,.14 Z";
    internal const string Wheel = "M .5,.17 C .455,.17 .433,.19 .433,.23 L .433,.36 C .433,.40 .455,.42 .5,.42 C .545,.42 .567,.40 .567,.36 L .567,.23 C .567,.19 .545,.17 .5,.17 Z";
    internal const string Palm = "M .057,.49 L .943,.49 C .953,.68 .926,.82 .82,.91 C .744,.974 .63,.985 .5,.985 C .37,.985 .256,.974 .18,.91 C .074,.82 .047,.68 .057,.49 Z";
    internal const string SideFront = "M .021,.515 Q .021,.497 .042,.497 L .075,.497 Q .091,.497 .091,.515 L .091,.615 Q .091,.635 .074,.635 L .041,.635 Q .021,.635 .021,.615 Z";
    internal const string SideBack = "M .038,.678 Q .035,.658 .054,.655 L .084,.651 Q .102,.649 .106,.668 L .122,.762 Q .125,.781 .106,.785 L .076,.789 Q .058,.791 .054,.772 Z";
}
