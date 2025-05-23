﻿using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using FinalAPIDoAn.Data;
using FinalAPIDoAn.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace FinalAPIDoAn.Controllers
{
    [Route("api/products")]
    [ApiController]
    public class ProductController : ControllerBase
    {
        private readonly KetNoiCSDL _dbc;
        private readonly Cloudinary _cloudinary;
        private readonly ILogger<ProductController> _logger;

        public ProductController(
            KetNoiCSDL dbc,
            IConfiguration configuration,
            ILogger<ProductController> logger)
        {
            _dbc = dbc;
            _logger = logger;

            // Initialize Cloudinary
            var account = new Account(
                configuration["Cloudinary:CloudName"],
                configuration["Cloudinary:ApiKey"],
                configuration["Cloudinary:ApiSecret"]);
            _cloudinary = new Cloudinary(account);
        }

        // GET: api/products/List
        [HttpGet("List")]
        public async Task<IActionResult> GetAllProducts()
        {
            try
            {
                var products = await _dbc.Products
                    .Include(p => p.Category)
                    .Include(p => p.ProductImages)
                    .ToListAsync();

                return Ok(new { data = products });
            }
            catch (Exception ex)
            {
                // Log the detailed error
                _logger.LogError(ex, "Error getting all products");

                // Return the exception details with the actual error status code
                return StatusCode(500, new
                {
                    message = "Internal server error",
                    errorCode = 500,
                    exceptionMessage = ex.Message,
                    stackTrace = ex.StackTrace
                });
            }
        }


        // GET: api/products/Search?keyword=...
        [HttpGet("Search")]
        public async Task<IActionResult> SearchProducts([FromQuery] string keyword)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(keyword))
                {
                    return BadRequest(new { message = "Search keyword is required" });
                }

                var results = await _dbc.Products
                    .Include(p => p.ProductImages)
                    .Where(p => p.ProductName.Contains(keyword) ||
                               p.Description.Contains(keyword))
                    .ToListAsync();

                return Ok(new { data = results });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error searching products with keyword: {keyword}");
                return StatusCode(500, "Internal server error");
            }
        }

        // GET: api/products/Get/5
        [HttpGet("Get/{id}")]
        public async Task<IActionResult> GetProductById(int id)
        {
            try
            {
                var product = await _dbc.Products
                    .Include(p => p.Category)
                    .Include(p => p.ProductImages)
                    .FirstOrDefaultAsync(p => p.ProductId == id);

                if (product == null)
                {
                    return NotFound(new { message = "Product not found" });
                }

                return Ok(new { data = product });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting product with ID {id}");
                return StatusCode(500, "Internal server error");
            }
        }

        // POST: api/products/Add
        [HttpPost("Add")]
        public async Task<IActionResult> AddProduct([FromForm] ProductDto productDto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            using var transaction = await _dbc.Database.BeginTransactionAsync();

            try
            {
                var product = new Product
                {
                    ProductName = productDto.ProductName,
                    Description = productDto.Description,
                    Price = productDto.Price,
                    StockQuantity = productDto.StockQuantity,
                    CategoryId = productDto.CategoryID,
                    CreatedAt = DateTime.UtcNow
                };

                _dbc.Products.Add(product);
                await _dbc.SaveChangesAsync();

                // Handle main image upload
                if (productDto.MainImage != null)
                {
                    var uploadResult = await UploadImageToCloudinary(productDto.MainImage, product.ProductId);
                    if (uploadResult != null)
                    {
                        _dbc.ProductImages.Add(new ProductImage
                        {
                            ProductId = product.ProductId,
                            ImageUrl = uploadResult.SecureUrl.ToString(),
                            PublicId = uploadResult.PublicId,
                            IsDefault = true,
                            ImageOrder = 0,
                            CreatedAt = DateTime.UtcNow
                        });
                    }
                }

                // Handle additional images
                if (productDto.AdditionalImages != null && productDto.AdditionalImages.Any())
                {
                    int order = 1;
                    foreach (var image in productDto.AdditionalImages)
                    {
                        var uploadResult = await UploadImageToCloudinary(image, product.ProductId);
                        if (uploadResult != null)
                        {
                            _dbc.ProductImages.Add(new ProductImage
                            {
                                ProductId = product.ProductId,
                                ImageUrl = uploadResult.SecureUrl.ToString(),
                                PublicId = uploadResult.PublicId,
                                IsDefault = false,
                                ImageOrder = order++,
                                CreatedAt = DateTime.UtcNow
                            });
                        }
                    }
                }

                await _dbc.SaveChangesAsync();
                await transaction.CommitAsync();

                return CreatedAtAction(nameof(GetProductById),
                    new { id = product.ProductId },
                    new { data = product });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error adding product");
                return StatusCode(500, "Internal server error");
            }
        }

        // PUT: api/products/Update/5
        [HttpPut("Update/{id}")]
        public async Task<IActionResult> UpdateProduct(int id, [FromForm] ProductDto productDto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            using var transaction = await _dbc.Database.BeginTransactionAsync();

            try
            {
                var product = await _dbc.Products
                    .Include(p => p.ProductImages)
                    .FirstOrDefaultAsync(p => p.ProductId == id);

                if (product == null)
                {
                    return NotFound(new { message = "Product not found" });
                }

                // Update product properties
                product.ProductName = productDto.ProductName;
                product.Description = productDto.Description;
                product.Price = productDto.Price;
                product.StockQuantity = productDto.StockQuantity;
                product.CategoryId = productDto.CategoryID;

                // Handle main image update
                if (productDto.MainImage != null)
                {
                    // Remove existing default image
                    var oldMainImage = product.ProductImages.FirstOrDefault(pi => pi.IsDefault == true);
                    if (oldMainImage != null)
                    {
                        await DeleteImageFromCloudinary(oldMainImage.PublicId);
                        _dbc.ProductImages.Remove(oldMainImage);
                    }

                    // Upload new main image
                    var uploadResult = await UploadImageToCloudinary(productDto.MainImage, product.ProductId);
                    if (uploadResult != null)
                    {
                        _dbc.ProductImages.Add(new ProductImage
                        {
                            ProductId = product.ProductId,
                            ImageUrl = uploadResult.SecureUrl.ToString(),
                            PublicId = uploadResult.PublicId,
                            IsDefault = true,
                            ImageOrder = 0,
                            CreatedAt = DateTime.UtcNow
                        });
                    }
                }

                // Handle additional images
                if (productDto.AdditionalImages != null && productDto.AdditionalImages.Any())
                {
                    var maxOrder = product.ProductImages.Max(pi => pi.ImageOrder) ?? 0;
                    foreach (var image in productDto.AdditionalImages)
                    {
                        var uploadResult = await UploadImageToCloudinary(image, product.ProductId);
                        if (uploadResult != null)
                        {
                            _dbc.ProductImages.Add(new ProductImage
                            {
                                ProductId = product.ProductId,
                                ImageUrl = uploadResult.SecureUrl.ToString(),
                                PublicId = uploadResult.PublicId,
                                IsDefault = false,
                                ImageOrder = ++maxOrder,
                                CreatedAt = DateTime.UtcNow
                            });
                        }
                    }
                }

                await _dbc.SaveChangesAsync();
                await transaction.CommitAsync();

                return Ok(new { data = product });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, $"Error updating product with ID {id}");
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpDelete("Delete/{id}")]
        public async Task<IActionResult> DeleteProduct(int id)
        {
            using var transaction = await _dbc.Database.BeginTransactionAsync();
            try
            {
                // Lấy sản phẩm cần xoá kèm theo các ảnh liên quan (nếu có)
                var product = await _dbc.Products
                    .Include(p => p.ProductImages)
                    .FirstOrDefaultAsync(p => p.ProductId == id);

                if (product == null)
                    return NotFound(new { message = "Product not found." });

                // Xoá các ProductDiscount liên kết với sản phẩm này
                var productDiscounts = _dbc.ProductDiscounts.Where(pd => pd.ProductId == id).ToList();
                if (productDiscounts.Any())
                {
                    _dbc.ProductDiscounts.RemoveRange(productDiscounts);
                }

                // Xoá các ảnh sản phẩm (với ví dụ xoá trên Cloudinary)
                if (product.ProductImages != null && product.ProductImages.Any())
                {
                    foreach (var image in product.ProductImages)
                    {
                        try
                        {
                            await DeleteImageFromCloudinary(image.PublicId);
                            _dbc.ProductImages.Remove(image);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error deleting image with PublicId {PublicId}", image.PublicId);
                            // Có thể lựa chọn bỏ qua lỗi nếu không quá quan trọng
                        }
                    }
                }

                // Cuối cùng xoá sản phẩm
                _dbc.Products.Remove(product);
                await _dbc.SaveChangesAsync();
                await transaction.CommitAsync();

                return Ok(new { message = "Product data deleted successfully." });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error deleting product with ID {id}", id);
                return StatusCode(500, new { message = "Internal server error." });
            }
        }

        // POST: api/products/5/images
        [HttpPost("{productId}/images")]
        public async Task<IActionResult> AddProductImages(int productId, List<IFormFile> files)
        {
            try
            {
                var product = await _dbc.Products.FindAsync(productId);
                if (product == null)
                {
                    return NotFound("Product not found");
                }

                var uploadResults = new List<ProductImage>();
                var currentMaxOrder = await _dbc.ProductImages
                    .Where(pi => pi.ProductId == productId)
                    .MaxAsync(pi => (int?)pi.ImageOrder) ?? 0;

                foreach (var file in files)
                {
                    var result = await UploadImageToCloudinary(file, productId);
                    if (result != null)
                    {
                        var image = new ProductImage
                        {
                            ProductId = productId,
                            ImageUrl = result.SecureUrl.ToString(),
                            PublicId = result.PublicId,
                            IsDefault = false,
                            ImageOrder = ++currentMaxOrder,
                            CreatedAt = DateTime.UtcNow
                        };

                        _dbc.ProductImages.Add(image);
                        uploadResults.Add(image);
                    }
                }

                await _dbc.SaveChangesAsync();
                return Ok(uploadResults);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error adding images to product {productId}");
                return StatusCode(500, "Internal server error");
            }
        }

        // DELETE: api/products/5/images/10
        [HttpDelete("{productId}/images/{imageId}")]
        public async Task<IActionResult> DeleteProductImage(int productId, int imageId)
        {
            try
            {
                var image = await _dbc.ProductImages
                    .FirstOrDefaultAsync(pi => pi.ImageId == imageId && pi.ProductId == productId);

                if (image == null)
                {
                    return NotFound("Image not found");
                }

                await DeleteImageFromCloudinary(image.PublicId);
                _dbc.ProductImages.Remove(image);
                await _dbc.SaveChangesAsync();

                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting image {imageId} from product {productId}");
                return StatusCode(500, "Internal server error");
            }
        }

        private async Task<ImageUploadResult?> UploadImageToCloudinary(IFormFile file, int productId)
        {
            if (file == null || file.Length == 0)
            {
                return null;
            }

            try
            {
                await using var stream = file.OpenReadStream();
                var uploadParams = new ImageUploadParams
                {
                    File = new FileDescription(file.FileName, stream),
                    Folder = $"products/{productId}",
                    Transformation = new Transformation()
                        .Height(1000).Width(1000).Crop("limit")
                        .Quality("auto")
                };

                return await _cloudinary.UploadAsync(uploadParams);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading image to Cloudinary");
                return null;
            }
        }

        private async Task DeleteImageFromCloudinary(string publicId)
        {
            if (string.IsNullOrEmpty(publicId))
            {
                return;
            }

            try
            {
                await _cloudinary.DestroyAsync(new DeletionParams(publicId));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting image from Cloudinary: {publicId}");
            }
        }
    }

    public class ProductDto
    {
        [Required(ErrorMessage = "Product name is required")]
        public required string ProductName { get; set; }

        [Required(ErrorMessage = "Description is required")]
        public required string Description { get; set; }

        [Required(ErrorMessage = "Price is required")]
        [Range(0.01, double.MaxValue, ErrorMessage = "Price must be greater than 0")]
        public decimal Price { get; set; }

        [Required(ErrorMessage = "Stock quantity is required")]
        [Range(0, int.MaxValue, ErrorMessage = "Stock quantity cannot be negative")]
        public int StockQuantity { get; set; }

        [Required(ErrorMessage = "Category ID is required")]
        public int CategoryID { get; set; }

        public IFormFile? MainImage { get; set; }
        public List<IFormFile>? AdditionalImages { get; set; }
    }
}