APP_SRC := $(PWD)/totp-service/totpFunction/src/totpFunction
DOTNET_IMAGE := mcr.microsoft.com/dotnet/sdk:8.0
BUILD_DIR := $(PWD)/.terraform_build
TERRAFORM_DIR := $(PWD)/terraform

.PHONY: clean
clean:
	@rm -rf $(BUILD_DIR)

.PHONY: build
build: clean
	@mkdir -p $(BUILD_DIR)
	@docker run --rm -v $(APP_SRC):/app -w /app $(DOTNET_IMAGE) dotnet publish -c Release -r linux-x64 -o publish
	@cd $(APP_SRC)/publish && zip -q -r $(BUILD_DIR)/LambdaDeployment.zip .

.PHONY: deploy
deploy: 
	@cd $(TERRAFORM_DIR) && terraform init && terraform apply --auto-approve

.PHONY: test
test:
	@docker compose up -d
	@docker compose exec dotnet-dev dotnet test totpFunction/test/totpFunction.Tests
    