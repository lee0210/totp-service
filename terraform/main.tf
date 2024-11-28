provider "aws" {
  default_tags {
    tags = {
      "Terraform"       = "true"
      "Terraform_stack" = var.stack_name
    }
  }
}

data "aws_iam_policy_document" "lambda_assume_role_policy" {
  statement {
    actions = ["sts:AssumeRole"]

    principals {
      type        = "Service"
      identifiers = ["lambda.amazonaws.com"]
    }
  }
}

resource "aws_iam_role" "function_role" {
  name               = "${var.stack_name}-totp-service-function-role"
  assume_role_policy = data.aws_iam_policy_document.lambda_assume_role_policy.json
}

resource "aws_lambda_function" "totp_function" {
  function_name = "${var.stack_name}-totp-function"
  handler       = "totpFunction::TOTPFunction.Function::FunctionHandler"
  runtime       = "dotnet8"
  filename      = "${path.module}/../.terraform_build/LambdaDeployment.zip"
  role          = aws_iam_role.function_role.arn
  memory_size   = 1024
  environment {
    variables = {
      TABLE_PREFIX = "${var.stack_name}-"
    }
  }
}

resource "aws_iam_role_policy_attachment" "lambda_basic_execution_role_attachment" {
  role       = aws_iam_role.function_role.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AWSLambdaBasicExecutionRole"
}

resource "aws_dynamodb_table" "totps_table" {
  name         = "${var.stack_name}-Totps"
  billing_mode = "PAY_PER_REQUEST"
  on_demand_throughput {
    max_read_request_units  = 5
    max_write_request_units = 5
  }
  hash_key = "Id"
  attribute {
    name = "Id"
    type = "S"
  }
}

data "aws_iam_policy_document" "allow_access_dynamodb" {
  statement {
    actions = [
      "dynamodb:DescribeTable",
      "dynamodb:BatchGetItem",
      "dynamodb:BatchWriteItem",
      "dynamodb:DeleteItem",
      "dynamodb:GetItem",
      "dynamodb:PutItem",
      "dynamodb:Query",
      "dynamodb:Scan",
      "dynamodb:UpdateItem"
    ]

    resources = [
      aws_dynamodb_table.totps_table.arn
    ]
  }
}

resource "aws_iam_policy" "allow_access_dynamodb" {
  name        = "${var.stack_name}-allow-access-dynamodb"
  description = "Allow access to DynamoDB tables"
  policy      = data.aws_iam_policy_document.allow_access_dynamodb.json
}

resource "aws_iam_role_policy_attachment" "allow_access_dynamodb_policy_attachment" {
  role       = aws_iam_role.function_role.name
  policy_arn = aws_iam_policy.allow_access_dynamodb.arn
}