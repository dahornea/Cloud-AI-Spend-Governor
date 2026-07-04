resource "azurerm_redis_cache" "session_cache" {
  name     = "session-cache-dev"
  location = "westeurope"
  capacity = 1
  family   = "P"
  sku_name = "Premium"

  tags = {
    environment = "dev"
    owner       = "platform"
  }
}
