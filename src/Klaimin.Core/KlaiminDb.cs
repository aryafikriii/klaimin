using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Klaimin.Core;

public class KlaiminDb(DbContextOptions<KlaiminDb> options) : IdentityDbContext<AppUser>(options);
