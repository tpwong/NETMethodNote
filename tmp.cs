// Mappings/MappingConfig.cs
using Mapster;
using Microsoft.EntityFrameworkCore;

public class MappingConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        // 這是需要 DI 服務的對應規則
        config.ForType<Product, ProductDto>()
            .Map(dest => dest.CategoryName,
                 // 使用 MapContext 來安全地獲取 Scoped 服務
                 src => MapContext.Current.GetService<ApplicationDbContext>()
                                  .Categories
                                  .AsNoTracking() // 效能優化：僅查詢，不追蹤
                                  .FirstOrDefault(c => c.Id == src.CategoryId)
                                  .Name);
        
        // 您可以在這裡加入其他不需要 DI 服務的常規對應
        config.ForType<OtherSource, OtherDestination>()
            .Map(dest => dest.FullName, src => $"{src.FirstName} {src.LastName}");
    }
}