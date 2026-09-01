@description('The location used for all deployed resources')
param location string = resourceGroup().location

@description('Tags that will be applied to all resources')
param tags object = {}


param quotesApiExists bool

@description('Id of the user or app to assign application roles')
param principalId string

@description('Principal type of user or app')
param principalType string

@secure()
@description('JWT signing key for the QuotesApi service')
param jwtKey string

@secure()
@description('Application Insights connection string for the QuotesApi service (Day 5 Task 5)')
param appInsightsConnectionString string

// Existing Container Registry (Day 5 Task 3 - thinkschool-rg)
resource containerRegistry 'Microsoft.ContainerRegistry/registries@2023-01-01-preview' existing = {
  name: 'thinkschoolacr'
}

// Existing Container Apps environment (Day 5 Task 3 - thinkschool-rg)
resource containerAppsEnvironment 'Microsoft.App/managedEnvironments@2023-05-01' existing = {
  name: 'thinkschool-env'
}

module quotesApiFetchLatestImage './modules/fetch-container-image.bicep' = {
  name: 'quotesApi-fetch-image'
  params: {
    exists: quotesApiExists
    name: 'quotes-api'
  }
}

module quotesApi 'br/public:avm/res/app/container-app:0.8.0' = {
  name: 'quotesApi'
  params: {
    name: 'quotes-api'
    ingressTargetPort: 8080
    ingressAllowInsecure: false
    scaleMinReplicas: 1
    scaleMaxReplicas: 3
    secrets: {
      secureList: [
        {
          name: 'jwt-key'
          value: jwtKey
        }
        {
          name: 'appinsights-connection-string'
          value: appInsightsConnectionString
        }
      ]
    }
    containers: [
      {
        image: quotesApiFetchLatestImage.outputs.?containers[?0].?image ?? 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'
        name: 'main'
        resources: {
          cpu: json('0.5')
          memory: '1.0Gi'
        }
        env: [
          {
            name: 'PORT'
            value: '8080'
          }
          {
            name: 'Jwt__Key'
            secretRef: 'jwt-key'
          }
          {
            name: 'AppInsights__ConnectionString'
            secretRef: 'appinsights-connection-string'
          }
        ]
      }
    ]
    managedIdentities: {
      systemAssigned: true
    }
    registries: [
      {
        server: containerRegistry.properties.loginServer
        identity: 'system'
      }
    ]
    environmentResourceId: containerAppsEnvironment.id
    location: location
    tags: union(tags, { 'azd-service-name': 'quotes-api' })
  }
}
output AZURE_CONTAINER_REGISTRY_ENDPOINT string = containerRegistry.properties.loginServer
output AZURE_RESOURCE_QUOTES_API_ID string = quotesApi.outputs.resourceId
