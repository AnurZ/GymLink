using GymLink.Domain.Identity;
using Microsoft.AspNetCore.Identity;

namespace GymLink.Infrastructure.Identity;

public sealed class GymLinkIdentityUser : IdentityUser<Guid>
{
    public GymLinkIdentityUser()
    {
        Id = Guid.NewGuid();
        SecurityStamp = Guid.NewGuid().ToString("N");
    }

    public UserProfile Profile { get; set; } = null!;
}
