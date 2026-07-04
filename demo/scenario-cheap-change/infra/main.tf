resource "azurerm_linux_virtual_machine" "worker" {
  name     = "worker-dev"
  location = "westeurope"
  size     = "Standard_B2s"

  tags = {
    environment = "dev"
    owner       = "platform"
  }
}
