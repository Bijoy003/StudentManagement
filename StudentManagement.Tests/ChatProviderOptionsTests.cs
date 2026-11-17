using StudentManagement.Configuration;

namespace StudentManagement.Tests;

public class ChatProviderOptionsTests
{
    [Fact]
    public void ResolveChatProvider_WithoutProviders_ReturnsFlatOptions()
    {
        // Arrange
        var options = new ChatOptions
        {
            Endpoint = "http://localhost:11434",
            Model = "qwen2.5:3b-instruct",
            ApiKey = "unused"
        };

        // Act
        var provider = options.ResolveChatProvider();

        // Assert
        Assert.Equal("http://localhost:11434", provider.Endpoint);
        Assert.Equal("qwen2.5:3b-instruct", provider.Model);
        Assert.Equal("unused", provider.ApiKey);
    }

    [Fact]
    public void ResolveChatProvider_WithActiveProvider_ReturnsProviderValues()
    {
        // Arrange
        var options = new ChatOptions
        {
            ActiveProvider = "OpenCode",
            Endpoint = "http://localhost:11434",
            Model = "qwen2.5:3b-instruct",
            Providers =
            {
                ["Ollama"] = new ChatProviderOptions
                {
                    Endpoint = "http://localhost:11434",
                    Model = "qwen2.5:3b-instruct",
                    ApiKey = "unused"
                },
                ["OpenCode"] = new ChatProviderOptions
                {
                    Endpoint = "https://opencode.ai/inference/openai",
                    Model = "glm-5.3-flash",
                    ApiKey = "secret"
                }
            }
        };

        // Act
        var provider = options.ResolveChatProvider();

        // Assert
        Assert.Equal("https://opencode.ai/inference/openai", provider.Endpoint);
        Assert.Equal("glm-5.3-flash", provider.Model);
        Assert.Equal("secret", provider.ApiKey);
    }

    [Fact]
    public void ResolveChatProvider_ActiveProviderDifferentCase_ReturnsProviderValues()
    {
        // Arrange
        var options = new ChatOptions
        {
            ActiveProvider = "opencode",
            Providers =
            {
                ["OpenCode"] = new ChatProviderOptions
                {
                    Endpoint = "https://opencode.ai/inference/openai",
                    Model = "glm-5.3-flash",
                    ApiKey = "secret"
                }
            }
        };

        // Act
        var provider = options.ResolveChatProvider();

        // Assert
        Assert.Equal("glm-5.3-flash", provider.Model);
    }

    [Fact]
    public void ResolveChatProvider_UnknownProvider_Throws()
    {
        // Arrange
        var options = new ChatOptions
        {
            ActiveProvider = "Nope",
            Providers =
            {
                ["Ollama"] = new ChatProviderOptions
                {
                    Endpoint = "http://localhost:11434",
                    Model = "qwen2.5:3b-instruct"
                }
            }
        };

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() => options.ResolveChatProvider());

        // Assert
        Assert.Contains("Nope", exception.Message);
        Assert.Contains("Ollama", exception.Message);
    }

    [Fact]
    public void ResolveEmbeddingProvider_WithoutOverride_ReturnsActiveProvider()
    {
        // Arrange
        var options = new ChatOptions
        {
            ActiveProvider = "OpenCode",
            Providers =
            {
                ["Ollama"] = new ChatProviderOptions
                {
                    Endpoint = "http://localhost:11434",
                    Model = "qwen2.5:3b-instruct",
                    ApiKey = "unused"
                },
                ["OpenCode"] = new ChatProviderOptions
                {
                    Endpoint = "https://opencode.ai/inference/openai",
                    Model = "glm-5.3-flash",
                    ApiKey = "secret"
                }
            }
        };

        // Act
        var provider = options.ResolveEmbeddingProvider();

        // Assert
        Assert.Equal("https://opencode.ai/inference/openai", provider.Endpoint);
        Assert.Equal("secret", provider.ApiKey);
    }

    [Fact]
    public void ResolveEmbeddingProvider_WithOverride_ReturnsEmbeddingProvider()
    {
        // Arrange
        var options = new ChatOptions
        {
            ActiveProvider = "OpenCode",
            EmbeddingProvider = "Ollama",
            Providers =
            {
                ["Ollama"] = new ChatProviderOptions
                {
                    Endpoint = "http://localhost:11434",
                    Model = "qwen2.5:3b-instruct",
                    ApiKey = "unused"
                },
                ["OpenCode"] = new ChatProviderOptions
                {
                    Endpoint = "https://opencode.ai/inference/openai",
                    Model = "glm-5.3-flash",
                    ApiKey = "secret"
                }
            }
        };

        // Act
        var provider = options.ResolveEmbeddingProvider();

        // Assert
        Assert.Equal("http://localhost:11434", provider.Endpoint);
        Assert.Equal("unused", provider.ApiKey);
    }

    [Fact]
    public void Validate_DefaultOptions_ReturnsWithoutThrowing()
    {
        // Arrange
        var options = new ChatOptions();

        // Act
        var exception = Record.Exception(options.Validate);

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public void Validate_ProvidersWithoutActiveProvider_ReturnsWithoutThrowing()
    {
        // Arrange — no ActiveProvider means the flat Chat settings stay authoritative,
        // so existing appsettings overrides keep working after profiles are introduced.
        var options = new ChatOptions
        {
            ActiveProvider = "",
            Providers =
            {
                ["Ollama"] = new ChatProviderOptions
                {
                    Endpoint = "http://localhost:11434",
                    Model = "qwen2.5:3b-instruct"
                }
            }
        };

        // Act
        var exception = Record.Exception(options.Validate);

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public void ResolveChatProvider_ProvidersWithoutActiveProvider_ReturnsFlatOptions()
    {
        // Arrange
        var options = new ChatOptions
        {
            Endpoint = "http://host.docker.internal:1234",
            Model = "lmstudio-model",
            Providers =
            {
                ["Ollama"] = new ChatProviderOptions
                {
                    Endpoint = "http://localhost:11434",
                    Model = "qwen2.5:3b-instruct"
                }
            }
        };

        // Act
        var provider = options.ResolveChatProvider();

        // Assert
        Assert.Equal("http://host.docker.internal:1234", provider.Endpoint);
        Assert.Equal("lmstudio-model", provider.Model);
    }

    [Fact]
    public void Validate_UnknownActiveProvider_Throws()
    {
        // Arrange
        var options = new ChatOptions
        {
            ActiveProvider = "Nope",
            Providers =
            {
                ["Ollama"] = new ChatProviderOptions
                {
                    Endpoint = "http://localhost:11434",
                    Model = "qwen2.5:3b-instruct"
                }
            }
        };

        // Act
        var exception = Assert.Throws<InvalidOperationException>(options.Validate);

        // Assert
        Assert.Contains("Nope", exception.Message);
    }

    [Fact]
    public void Validate_ActiveProviderWithoutEndpoint_Throws()
    {
        // Arrange
        var options = new ChatOptions
        {
            ActiveProvider = "OpenCode",
            Providers =
            {
                ["OpenCode"] = new ChatProviderOptions
                {
                    Endpoint = "",
                    Model = "glm-5.3-flash"
                }
            }
        };

        // Act
        var exception = Assert.Throws<InvalidOperationException>(options.Validate);

        // Assert
        Assert.Contains("Endpoint", exception.Message);
    }

    [Fact]
    public void Validate_ActiveProviderWithoutModel_Throws()
    {
        // Arrange
        var options = new ChatOptions
        {
            ActiveProvider = "OpenCode",
            Providers =
            {
                ["OpenCode"] = new ChatProviderOptions
                {
                    Endpoint = "https://opencode.ai/inference/openai",
                    Model = ""
                }
            }
        };

        // Act
        var exception = Assert.Throws<InvalidOperationException>(options.Validate);

        // Assert
        Assert.Contains("Model", exception.Message);
    }

    [Fact]
    public void Validate_RemoteProviderWithoutApiKey_ReturnsWithoutThrowing()
    {
        // Arrange — a missing key surfaces as a runtime 401, never as a startup failure.
        var options = new ChatOptions
        {
            ActiveProvider = "OpenCode",
            Providers =
            {
                ["OpenCode"] = new ChatProviderOptions
                {
                    Endpoint = "https://opencode.ai/inference/openai",
                    Model = "glm-5.3-flash",
                    ApiKey = ""
                }
            }
        };

        // Act
        var exception = Record.Exception(options.Validate);

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public void ResolveEmbeddingProvider_UnknownOverride_Throws()
    {
        // Arrange
        var options = new ChatOptions
        {
            ActiveProvider = "Ollama",
            EmbeddingProvider = "Nope",
            Providers =
            {
                ["Ollama"] = new ChatProviderOptions
                {
                    Endpoint = "http://localhost:11434",
                    Model = "qwen2.5:3b-instruct"
                }
            }
        };

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() => options.ResolveEmbeddingProvider());

        // Assert
        Assert.Contains("Nope", exception.Message);
        Assert.Contains("Ollama", exception.Message);
    }

    [Fact]
    public void ResolveEmbeddingProvider_OverrideDifferentCase_ReturnsProviderValues()
    {
        // Arrange
        var options = new ChatOptions
        {
            ActiveProvider = "OpenCode",
            EmbeddingProvider = "ollama",
            Providers =
            {
                ["Ollama"] = new ChatProviderOptions
                {
                    Endpoint = "http://localhost:11434",
                    Model = "nomic-embed-text"
                },
                ["OpenCode"] = new ChatProviderOptions
                {
                    Endpoint = "https://opencode.ai/inference/openai",
                    Model = "glm-5.3-flash"
                }
            }
        };

        // Act
        var provider = options.ResolveEmbeddingProvider();

        // Assert
        Assert.Equal("nomic-embed-text", provider.Model);
    }

    [Fact]
    public void Validate_UnknownEmbeddingOverride_Throws()
    {
        // Arrange
        var options = new ChatOptions
        {
            ActiveProvider = "Ollama",
            EmbeddingProvider = "Nope",
            Providers =
            {
                ["Ollama"] = new ChatProviderOptions
                {
                    Endpoint = "http://localhost:11434",
                    Model = "qwen2.5:3b-instruct"
                }
            }
        };

        // Act
        var exception = Assert.Throws<InvalidOperationException>(options.Validate);

        // Assert
        Assert.Contains("Nope", exception.Message);
    }

    [Fact]
    public void Validate_EmbeddingProviderWithoutEndpoint_Throws()
    {
        // Arrange
        var options = new ChatOptions
        {
            ActiveProvider = "OpenCode",
            EmbeddingProvider = "Ollama",
            Providers =
            {
                ["OpenCode"] = new ChatProviderOptions
                {
                    Endpoint = "https://opencode.ai/inference/openai",
                    Model = "glm-5.3-flash"
                },
                ["Ollama"] = new ChatProviderOptions
                {
                    Endpoint = "",
                    Model = "nomic-embed-text"
                }
            }
        };

        // Act
        var exception = Assert.Throws<InvalidOperationException>(options.Validate);

        // Assert
        Assert.Contains("Ollama", exception.Message);
        Assert.Contains("Endpoint", exception.Message);
    }

    [Fact]
    public void Validate_EmbeddingProviderWithoutModel_Throws()
    {
        // Arrange
        var options = new ChatOptions
        {
            ActiveProvider = "OpenCode",
            EmbeddingProvider = "Ollama",
            Providers =
            {
                ["OpenCode"] = new ChatProviderOptions
                {
                    Endpoint = "https://opencode.ai/inference/openai",
                    Model = "glm-5.3-flash"
                },
                ["Ollama"] = new ChatProviderOptions
                {
                    Endpoint = "http://localhost:11434",
                    Model = ""
                }
            }
        };

        // Act
        var exception = Assert.Throws<InvalidOperationException>(options.Validate);

        // Assert
        Assert.Contains("Ollama", exception.Message);
        Assert.Contains("Model", exception.Message);
    }

    [Fact]
    public void Validate_ValidEmbeddingOverride_ReturnsWithoutThrowing()
    {
        // Arrange
        var options = new ChatOptions
        {
            ActiveProvider = "OpenCode",
            EmbeddingProvider = "Ollama",
            Providers =
            {
                ["OpenCode"] = new ChatProviderOptions
                {
                    Endpoint = "https://opencode.ai/inference/openai",
                    Model = "glm-5.3-flash"
                },
                ["Ollama"] = new ChatProviderOptions
                {
                    Endpoint = "http://localhost:11434",
                    Model = "nomic-embed-text"
                }
            }
        };

        // Act
        var exception = Record.Exception(options.Validate);

        // Assert
        Assert.Null(exception);
    }
}
