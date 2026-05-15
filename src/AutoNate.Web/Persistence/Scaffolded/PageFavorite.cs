using System;

namespace AutoNate.Web.Persistence.Scaffolded;

public partial class PageFavorite
{
    public Guid PageId { get; set; }

    public Guid UserId { get; set; }

    public DateTime FavoritedAtUtc { get; set; }
}
