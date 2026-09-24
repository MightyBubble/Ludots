#include "ludots_effekseer.h"

#include <Effekseer.h>
#include <Effekseer/Effekseer.EffectImplemented.h>
#include <Effekseer/Effekseer.EffectNode.h>
#include <EffekseerRendererGL.h>
#include <EffekseerRendererCommon/EffekseerRenderer.PngTextureLoader.h>

#include <cmath>
#include <cstring>
#include <cstdio>
#include <exception>
#include <filesystem>
#include <fstream>
#include <limits>
#include <string>
#include <unordered_map>
#include <vector>

#if defined(_WIN32)
#define NOMINMAX
#include <Windows.h>
#endif

namespace
{
constexpr int32_t ApiVersion = LUDOTS_EFFEKSEER_BRIDGE_ABI_VERSION;
constexpr int32_t RequiredRuntimeFormatVersion = LUDOTS_EFFEKSEER_RUNTIME_FORMAT_VERSION;
constexpr int32_t DynamicInputSlotCount = LUDOTS_EFFEKSEER_DYNAMIC_INPUT_SLOT_COUNT;
constexpr const char* EffekseerVersion = LUDOTS_EFFEKSEER_VERSION;
thread_local std::string ThreadError;

struct Context
{
    Effekseer::ManagerRef Manager;
    EffekseerRendererGL::RendererRef Renderer;
    std::unordered_map<int32_t, Effekseer::EffectRef> Assets;
    std::string LastError;
    float TimeSeconds = 0.0f;
};

void SetError(Context* context, const std::string& message)
{
    ThreadError = message;
    if (context != nullptr)
    {
        context->LastError = message;
    }
}

void ClearError(Context* context)
{
    ThreadError.clear();
    if (context != nullptr)
    {
        context->LastError.clear();
    }
}

std::u16string Utf8ToUtf16(const char* value)
{
    if (value == nullptr || value[0] == '\0')
    {
        return {};
    }

#if defined(_WIN32)
    const int required = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, value, -1, nullptr, 0);
    if (required <= 1)
    {
        return {};
    }

    std::wstring wide(static_cast<size_t>(required), L'\0');
    if (MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, value, -1, wide.data(), required) == 0)
    {
        return {};
    }

    wide.pop_back();
    return std::u16string(reinterpret_cast<const char16_t*>(wide.data()), wide.size());
#else
    std::u16string result;
    for (const unsigned char ch : std::string(value))
    {
        if (ch >= 0x80)
        {
            return {};
        }
        result.push_back(static_cast<char16_t>(ch));
    }
    return result;
#endif
}

bool IsSupportedNodeType(int32_t value)
{
    return value == static_cast<int32_t>(Effekseer::EffectNodeType::Sprite) ||
           value == static_cast<int32_t>(Effekseer::EffectNodeType::Ribbon) ||
           value == static_cast<int32_t>(Effekseer::EffectNodeType::Ring) ||
           value == static_cast<int32_t>(Effekseer::EffectNodeType::Model) ||
           value == static_cast<int32_t>(Effekseer::EffectNodeType::Track);
}

bool ResolvePortableResourcePath(
    Context* context,
    const std::filesystem::path& assetDirectory,
    const char16_t* rawPath,
    const char* resourceKind,
    std::filesystem::path& fullPath)
{
    if (rawPath == nullptr || rawPath[0] == u'\0')
    {
        SetError(context, std::string(resourceKind) + " resource path is empty.");
        return false;
    }

    const std::filesystem::path relativePath(rawPath);
    if (relativePath.is_absolute())
    {
        SetError(context, std::string(resourceKind) + " resource path must be relative to the emitter asset.");
        return false;
    }

    for (const auto& segment : relativePath)
    {
        if (segment == std::filesystem::path(u".."))
        {
            SetError(context, std::string(resourceKind) + " resource path cannot traverse outside the emitter asset directory.");
            return false;
        }
    }

    fullPath = assetDirectory / relativePath;
    std::error_code error;
    if (!std::filesystem::is_regular_file(fullPath, error) || error)
    {
        SetError(context, std::string(resourceKind) + " resource file does not exist.");
        return false;
    }

    const auto length = std::filesystem::file_size(fullPath, error);
    if (error || length == 0)
    {
        SetError(context, std::string(resourceKind) + " resource file is empty or unreadable.");
        return false;
    }

    return true;
}

bool ReadResourceBytes(
    Context* context,
    const std::filesystem::path& fullPath,
    const char* resourceKind,
    std::vector<uint8_t>& bytes)
{
    std::ifstream stream(fullPath, std::ios::binary | std::ios::ate);
    if (!stream)
    {
        SetError(context, std::string(resourceKind) + " resource file could not be opened.");
        return false;
    }
    const std::streamsize length = stream.tellg();
    if (length <= 0 || length > (std::numeric_limits<int32_t>::max)())
    {
        SetError(context, std::string(resourceKind) + " resource file size is invalid.");
        return false;
    }
    bytes.resize(static_cast<size_t>(length));
    stream.seekg(0, std::ios::beg);
    if (!stream.read(reinterpret_cast<char*>(bytes.data()), length))
    {
        SetError(context, std::string(resourceKind) + " resource file could not be read completely.");
        return false;
    }
    return true;
}

bool ValidatePngResource(
    Context* context,
    const std::filesystem::path& fullPath,
    const char* resourceKind)
{
    if (fullPath.extension() != std::filesystem::path(u".png"))
    {
        SetError(context, std::string(resourceKind) + " resource must use the minimal PNG contract.");
        return false;
    }
    std::vector<uint8_t> bytes;
    if (!ReadResourceBytes(context, fullPath, resourceKind, bytes))
    {
        return false;
    }
    EffekseerRenderer::PngTextureLoader decoder;
    if (!decoder.Load(bytes.data(), static_cast<int32_t>(bytes.size()), false) ||
        decoder.GetWidth() <= 0 ||
        decoder.GetHeight() <= 0 ||
        decoder.GetData().empty())
    {
        SetError(context, std::string(resourceKind) + " resource could not be decoded as PNG.");
        return false;
    }
    decoder.Unload();
    return true;
}

bool ReadModelInt32(
    Context* context,
    const std::vector<uint8_t>& bytes,
    size_t& cursor,
    const char* field,
    int32_t& value)
{
    if (cursor > bytes.size() || sizeof(int32_t) > bytes.size() - cursor)
    {
        SetError(context, std::string("Model resource ended before ") + field + ".");
        return false;
    }
    std::memcpy(&value, bytes.data() + cursor, sizeof(int32_t));
    cursor += sizeof(int32_t);
    return true;
}

bool ConsumeModelBytes(
    Context* context,
    const std::vector<uint8_t>& bytes,
    size_t& cursor,
    size_t count,
    const char* field)
{
    if (cursor > bytes.size() || count > bytes.size() - cursor)
    {
        SetError(context, std::string("Model resource ended inside ") + field + ".");
        return false;
    }
    cursor += count;
    return true;
}

bool ValidateModelResource(Context* context, const std::filesystem::path& fullPath)
{
    if (fullPath.extension() != std::filesystem::path(u".efkmodel"))
    {
        SetError(context, "Model resource must use the minimal .efkmodel contract.");
        return false;
    }
    std::vector<uint8_t> bytes;
    if (!ReadResourceBytes(context, fullPath, "Model", bytes))
    {
        return false;
    }

    size_t cursor = 0;
    int32_t version = 0;
    int32_t scaleBits = 0;
    int32_t modelCount = 0;
    int32_t frameCount = 0;
    if (!ReadModelInt32(context, bytes, cursor, "version", version) ||
        version != Effekseer::Model::LatestVersion ||
        !ReadModelInt32(context, bytes, cursor, "scale", scaleBits) ||
        !ReadModelInt32(context, bytes, cursor, "model count", modelCount) ||
        !ReadModelInt32(context, bytes, cursor, "frame count", frameCount))
    {
        if (version != Effekseer::Model::LatestVersion)
        {
            SetError(context, "Model resource must use the latest supported .efkmodel version.");
        }
        return false;
    }
    (void)scaleBits;
    if (modelCount != 1 || frameCount <= 0)
    {
        SetError(context, "Model resource must contain exactly one model and at least one frame.");
        return false;
    }

    for (int32_t frame = 0; frame < frameCount; ++frame)
    {
        int32_t vertexCount = 0;
        if (!ReadModelInt32(context, bytes, cursor, "vertex count", vertexCount) || vertexCount <= 0)
        {
            SetError(context, "Model resource frame must contain vertices.");
            return false;
        }
        const size_t vertexBytes = static_cast<size_t>(vertexCount) * sizeof(Effekseer::Model::Vertex);
        if (vertexBytes / sizeof(Effekseer::Model::Vertex) != static_cast<size_t>(vertexCount) ||
            !ConsumeModelBytes(context, bytes, cursor, vertexBytes, "vertex data"))
        {
            return false;
        }

        int32_t faceCount = 0;
        if (!ReadModelInt32(context, bytes, cursor, "face count", faceCount) || faceCount <= 0)
        {
            SetError(context, "Model resource frame must contain faces.");
            return false;
        }
        const size_t faceBytes = static_cast<size_t>(faceCount) * sizeof(Effekseer::Model::Face);
        if (faceBytes / sizeof(Effekseer::Model::Face) != static_cast<size_t>(faceCount) ||
            cursor > bytes.size() ||
            faceBytes > bytes.size() - cursor)
        {
            SetError(context, "Model resource ended inside face data.");
            return false;
        }
        for (int32_t face = 0; face < faceCount; ++face)
        {
            Effekseer::Model::Face indices{};
            std::memcpy(&indices, bytes.data() + cursor + (static_cast<size_t>(face) * sizeof(indices)), sizeof(indices));
            for (const int32_t index : indices.Indexes)
            {
                if (index < 0 || index >= vertexCount)
                {
                    SetError(context, "Model resource contains a face index outside its vertex array.");
                    return false;
                }
            }
        }
        cursor += faceBytes;
    }
    if (cursor != bytes.size())
    {
        SetError(context, "Model resource contains trailing bytes outside the declared frames.");
        return false;
    }

    Effekseer::Model decoded(bytes.data(), static_cast<int32_t>(bytes.size()));
    if (decoded.GetFrameCount() != frameCount || decoded.GetVertexCount() <= 0 || decoded.GetFaceCount() <= 0)
    {
        SetError(context, "Model resource decoded without renderable geometry.");
        return false;
    }
    return true;
}

bool ValidateExternalResourceFiles(
    Context* context,
    const Effekseer::EffectRef& effect,
    const std::filesystem::path& assetDirectory)
{
    std::filesystem::path fullPath;
    for (int32_t i = 0; i < effect->GetColorImageCount(); ++i)
    {
        if (!ResolvePortableResourcePath(context, assetDirectory, effect->GetColorImagePath(i), "Color texture", fullPath))
        {
            return false;
        }
        if (!ValidatePngResource(context, fullPath, "Color texture")) return false;
    }
    for (int32_t i = 0; i < effect->GetNormalImageCount(); ++i)
    {
        if (!ResolvePortableResourcePath(context, assetDirectory, effect->GetNormalImagePath(i), "Normal texture", fullPath))
        {
            return false;
        }
        if (!ValidatePngResource(context, fullPath, "Normal texture")) return false;
    }
    for (int32_t i = 0; i < effect->GetDistortionImageCount(); ++i)
    {
        if (!ResolvePortableResourcePath(context, assetDirectory, effect->GetDistortionImagePath(i), "Distortion texture", fullPath))
        {
            return false;
        }
        if (!ValidatePngResource(context, fullPath, "Distortion texture")) return false;
    }
    for (int32_t i = 0; i < effect->GetModelCount(); ++i)
    {
        if (!ResolvePortableResourcePath(context, assetDirectory, effect->GetModelPath(i), "Model", fullPath))
        {
            return false;
        }
        if (!ValidateModelResource(context, fullPath)) return false;
    }

    return true;
}

bool ValidateEffect(Context* context, const Effekseer::EffectRef& effect, int32_t expectedNodeType)
{
    if (effect == nullptr)
    {
        SetError(context, "Effekseer rejected the asset binary.");
        return false;
    }

    if (!IsSupportedNodeType(expectedNodeType))
    {
        SetError(context, "The declared emitter node type is not supported.");
        return false;
    }

    if (effect->GetVersion() != RequiredRuntimeFormatVersion)
    {
        char buffer[160];
        std::snprintf(
            buffer,
            sizeof(buffer),
            "The asset runtime format is %d; Effekseer %s runtime format %d is required.",
            effect->GetVersion(),
            EffekseerVersion,
            RequiredRuntimeFormatVersion);
        SetError(context, buffer);
        return false;
    }

    Effekseer::EffectNode* root = effect->GetRoot();
    if (root == nullptr || root->GetType() != Effekseer::EffectNodeType::Root)
    {
        SetError(context, "The asset does not contain a valid Effekseer root node.");
        return false;
    }

    if (root->GetChildrenCount() != 1)
    {
        SetError(context, "A concrete emitter asset must contain exactly one root child.");
        return false;
    }

    Effekseer::EffectNode* child = root->GetChild(0);
    if (child == nullptr || static_cast<int32_t>(child->GetType()) != expectedNodeType)
    {
        const int32_t actual = child == nullptr ? -999 : static_cast<int32_t>(child->GetType());
        char buffer[192];
        std::snprintf(buffer, sizeof(buffer), "Declared emitter node type %d does not match asset node type %d at root/0.", expectedNodeType, actual);
        SetError(context, buffer);
        return false;
    }

    if (child->GetChildrenCount() != 0)
    {
        SetError(context, "A concrete emitter asset must be Root -> one leaf node; nested emitters are not allowed.");
        return false;
    }

    auto* node = static_cast<Effekseer::EffectNodeImplemented*>(child);
    auto* implementedEffect = static_cast<Effekseer::EffectImplemented*>(effect.Get());
    const Effekseer::ParameterLODs defaultLods{};
    const auto hasTrigger = [](const Effekseer::TriggerValues& trigger)
    {
        return trigger.type != Effekseer::TriggerType::None;
    };
    if (hasTrigger(node->TriggerParam.ToStartGeneration) ||
        hasTrigger(node->TriggerParam.ToStopGeneration) ||
        hasTrigger(node->TriggerParam.ToRemove) ||
        hasTrigger(node->CommonValues.Generation.TriggerToStart) ||
        hasTrigger(node->CommonValues.Generation.TriggerToStop) ||
        hasTrigger(node->CommonValues.Generation.TriggerToGenerate) ||
        hasTrigger(node->CommonValues.Removal.TriggerToRemove))
    {
        SetError(context, "Effekseer triggers are outside the minimal emitter contract; Performer owns orchestration.");
        return false;
    }
    if (node->LODsParam.MatchingLODs != defaultLods.MatchingLODs ||
        node->LODsParam.LODBehaviour != defaultLods.LODBehaviour)
    {
        SetError(context, "Effekseer node LOD rules are outside the minimal emitter contract; Core owns LOD.");
        return false;
    }
    if (node->KillParam.Type != Effekseer::KillType::None)
    {
        SetError(context, "Effekseer kill rules are outside the minimal emitter contract; Performer owns lifetime.");
        return false;
    }
    if (node->LocalForceField.HasValue || node->LocalForceField.IsGlobalEnabled)
    {
        SetError(context, "Effekseer force fields are outside the minimal emitter contract.");
        return false;
    }
    for (const auto& field : node->LocalForceField.LocalForceFields)
    {
        if (field.HasValue)
        {
            SetError(context, "Effekseer force fields are outside the minimal emitter contract.");
            return false;
        }
    }
    if (node->Collisions.IsGroundCollisionEnabled || node->Collisions.IsSceneCollisionWithExternal)
    {
        SetError(context, "Effekseer collision rules are outside the minimal emitter contract; gameplay owns collision.");
        return false;
    }
    if (node->GpuParticlesResource != nullptr)
    {
        SetError(context, "Effekseer GPU particles are outside the minimal emitter contract.");
        return false;
    }
    if (!implementedEffect->GetDynamicEquation().empty())
    {
        SetError(context, "Effekseer dynamic expressions are outside the minimal emitter contract; Performer parameters own runtime variation.");
        return false;
    }

    if (effect->GetWaveCount() != 0)
    {
        SetError(context, "Emitter assets cannot contain sound resources; Performer owns sound composition.");
        return false;
    }

    if (effect->GetMaterialCount() != 0 || effect->GetCurveCount() != 0 || effect->GetProceduralModelCount() != 0)
    {
        SetError(context, "Custom materials, external curves, and procedural models are outside the minimal emitter contract.");
        return false;
    }

    if (context != nullptr)
    {
        for (int32_t i = 0; i < effect->GetColorImageCount(); ++i)
        {
            if (effect->GetColorImage(i) == nullptr)
            {
                SetError(context, "Color texture resource could not be loaded.");
                return false;
            }
        }
        for (int32_t i = 0; i < effect->GetNormalImageCount(); ++i)
        {
            if (effect->GetNormalImage(i) == nullptr)
            {
                SetError(context, "Normal texture resource could not be loaded.");
                return false;
            }
        }
        for (int32_t i = 0; i < effect->GetDistortionImageCount(); ++i)
        {
            if (effect->GetDistortionImage(i) == nullptr)
            {
                SetError(context, "Distortion texture resource could not be loaded.");
                return false;
            }
        }
    }

    if (expectedNodeType == static_cast<int32_t>(Effekseer::EffectNodeType::Model))
    {
        if (effect->GetModelCount() != 1)
        {
            SetError(context, "ModelEmitter requires exactly one external model resource.");
            return false;
        }

        if (context != nullptr)
        {
            const auto model = effect->GetModel(0);
            if (model == nullptr)
            {
                SetError(context, "ModelEmitter external model resource could not be loaded.");
                return false;
            }

            if (model->GetFrameCount() <= 0 || model->GetVertexCount() <= 0 || model->GetFaceCount() <= 0)
            {
                SetError(context, "ModelEmitter external model resource contains no renderable geometry.");
                return false;
            }
        }
    }
    else if (effect->GetModelCount() != 0)
    {
        SetError(context, "Only ModelEmitter may contain a model resource.");
        return false;
    }

    return true;
}

bool RequireContext(void* raw, Context*& context)
{
    context = static_cast<Context*>(raw);
    if (context == nullptr || context->Manager == nullptr || context->Renderer == nullptr)
    {
        SetError(context, "Effekseer context is null or has already been destroyed.");
        return false;
    }
    return true;
}

bool RequireHandle(Context* context, int32_t handle)
{
    if (handle < 0 || !context->Manager->Exists(handle))
    {
        SetError(context, "Effekseer handle does not exist.");
        return false;
    }
    return true;
}
}

int32_t LUDOTS_EFK_CALL ludots_effekseer_get_api_version(void)
{
    return ApiVersion;
}

int32_t LUDOTS_EFK_CALL ludots_effekseer_get_runtime_format_version(void)
{
    return RequiredRuntimeFormatVersion;
}

int32_t LUDOTS_EFK_CALL ludots_effekseer_get_dynamic_input_slot_count(void)
{
    return DynamicInputSlotCount;
}

const char* LUDOTS_EFK_CALL ludots_effekseer_get_last_error(void* rawContext)
{
    auto* context = static_cast<Context*>(rawContext);
    return context != nullptr ? context->LastError.c_str() : ThreadError.c_str();
}

int32_t LUDOTS_EFK_CALL ludots_effekseer_validate_asset(const char* pathUtf8, int32_t expectedNodeType)
{
    try
    {
        ClearError(nullptr);
        const std::u16string path = Utf8ToUtf16(pathUtf8);
        if (path.empty())
        {
            SetError(nullptr, "Asset path is empty or is not valid UTF-8.");
            return 0;
        }

        const std::filesystem::path filePath(path);
        std::ifstream stream(filePath, std::ios::binary | std::ios::ate);
        if (!stream)
        {
            SetError(nullptr, "Emitter asset file could not be opened.");
            return 0;
        }

        const std::streamsize length = stream.tellg();
        if (length <= 0 || length > (std::numeric_limits<int32_t>::max)())
        {
            SetError(nullptr, "Emitter asset file is empty or exceeds the 32-bit Effekseer size limit.");
            return 0;
        }

        std::vector<uint8_t> bytes(static_cast<size_t>(length));
        stream.seekg(0, std::ios::beg);
        if (!stream.read(reinterpret_cast<char*>(bytes.data()), length))
        {
            SetError(nullptr, "Emitter asset file could not be read completely.");
            return 0;
        }

        const std::u16string materialPath = filePath.parent_path().u16string();
        auto setting = Effekseer::Setting::Create();
        auto effect = Effekseer::Effect::Create(
            setting,
            bytes.data(),
            static_cast<int32_t>(bytes.size()),
            1.0f,
            materialPath.c_str());
        if (!ValidateEffect(nullptr, effect, expectedNodeType))
        {
            return 0;
        }
        return ValidateExternalResourceFiles(nullptr, effect, filePath.parent_path()) ? 1 : 0;
    }
    catch (const std::exception& ex)
    {
        SetError(nullptr, std::string("Effekseer asset validation failed: ") + ex.what());
        return 0;
    }
    catch (...)
    {
        SetError(nullptr, "Effekseer asset validation failed with an unknown native exception.");
        return 0;
    }
}

void* LUDOTS_EFK_CALL ludots_effekseer_create(int32_t maxInstances, int32_t maxSquares)
{
    Context* context = nullptr;
    try
    {
        ClearError(nullptr);
        if (maxInstances <= 0 || maxSquares <= 0)
        {
            SetError(nullptr, "Effekseer capacities must be positive.");
            return nullptr;
        }

        context = new Context();
        context->Manager = Effekseer::Manager::Create(maxInstances);
        if (context->Manager == nullptr)
        {
            SetError(context, "Effekseer manager creation failed.");
            delete context;
            return nullptr;
        }

        auto graphicsDevice = EffekseerRendererGL::CreateGraphicsDevice(EffekseerRendererGL::OpenGLDeviceType::OpenGL3);
        context->Renderer = EffekseerRendererGL::Renderer::Create(graphicsDevice, maxSquares);
        if (context->Renderer == nullptr)
        {
            SetError(context, "Effekseer OpenGL3 renderer creation failed. A current OpenGL context is required.");
            delete context;
            return nullptr;
        }

        context->Renderer->SetRestorationOfStatesFlag(true);
        context->Manager->SetCoordinateSystem(Effekseer::CoordinateSystem::RH);
        context->Manager->SetSpriteRenderer(context->Renderer->CreateSpriteRenderer());
        context->Manager->SetRibbonRenderer(context->Renderer->CreateRibbonRenderer());
        context->Manager->SetRingRenderer(context->Renderer->CreateRingRenderer());
        context->Manager->SetTrackRenderer(context->Renderer->CreateTrackRenderer());
        context->Manager->SetModelRenderer(context->Renderer->CreateModelRenderer());
        context->Manager->SetTextureLoader(context->Renderer->CreateTextureLoader());
        context->Manager->SetModelLoader(context->Renderer->CreateModelLoader());
        context->Manager->SetMaterialLoader(context->Renderer->CreateMaterialLoader());
        context->Manager->SetCurveLoader(Effekseer::MakeRefPtr<Effekseer::CurveLoader>());
        ClearError(context);
        return context;
    }
    catch (const std::exception& ex)
    {
        SetError(context, std::string("Effekseer context creation failed: ") + ex.what());
        delete context;
        return nullptr;
    }
    catch (...)
    {
        SetError(context, "Effekseer context creation failed with an unknown native exception.");
        delete context;
        return nullptr;
    }
}

void LUDOTS_EFK_CALL ludots_effekseer_destroy(void* rawContext)
{
    auto* context = static_cast<Context*>(rawContext);
    if (context == nullptr)
    {
        return;
    }

    context->Manager->StopAllEffects();
    context->Assets.clear();
    context->Manager.Reset();
    context->Renderer.Reset();
    delete context;
}

int32_t LUDOTS_EFK_CALL ludots_effekseer_load_asset(void* rawContext, int32_t assetId, const char* pathUtf8, int32_t expectedNodeType)
{
    Context* context;
    if (!RequireContext(rawContext, context))
    {
        return 0;
    }

    try
    {
        ClearError(context);
        if (assetId <= 0)
        {
            SetError(context, "Emitter asset id must be positive.");
            return 0;
        }
        if (context->Assets.find(assetId) != context->Assets.end())
        {
            SetError(context, "Emitter asset id is already loaded.");
            return 0;
        }

        const std::u16string path = Utf8ToUtf16(pathUtf8);
        if (path.empty())
        {
            SetError(context, "Emitter asset path is empty or is not valid UTF-8.");
            return 0;
        }

        auto effect = Effekseer::Effect::Create(context->Manager, path.c_str());
        if (!ValidateEffect(context, effect, expectedNodeType))
        {
            return 0;
        }

        context->Assets.emplace(assetId, effect);
        return 1;
    }
    catch (const std::exception& ex)
    {
        SetError(context, std::string("Emitter asset load failed: ") + ex.what());
        return 0;
    }
    catch (...)
    {
        SetError(context, "Emitter asset load failed with an unknown native exception.");
        return 0;
    }
}

int32_t LUDOTS_EFK_CALL ludots_effekseer_unload_asset(void* rawContext, int32_t assetId)
{
    Context* context;
    if (!RequireContext(rawContext, context))
    {
        return 0;
    }
    ClearError(context);
    if (context->Assets.erase(assetId) != 1)
    {
        SetError(context, "Emitter asset id is not loaded.");
        return 0;
    }
    return 1;
}

int32_t LUDOTS_EFK_CALL ludots_effekseer_play(void* rawContext, int32_t assetId, float x, float y, float z)
{
    Context* context;
    if (!RequireContext(rawContext, context))
    {
        return -1;
    }
    ClearError(context);
    const auto it = context->Assets.find(assetId);
    if (it == context->Assets.end())
    {
        SetError(context, "Emitter asset id is not loaded.");
        return -1;
    }

    const int32_t handle = context->Manager->Play(it->second, x, y, z);
    if (handle < 0)
    {
        SetError(context, "Effekseer could not allocate an emitter handle.");
    }
    return handle;
}

int32_t LUDOTS_EFK_CALL ludots_effekseer_exists(void* rawContext, int32_t handle)
{
    Context* context;
    if (!RequireContext(rawContext, context))
    {
        return 0;
    }
    return handle >= 0 && context->Manager->Exists(handle) ? 1 : 0;
}

int32_t LUDOTS_EFK_CALL ludots_effekseer_stop(void* rawContext, int32_t handle)
{
    Context* context;
    if (!RequireContext(rawContext, context) || !RequireHandle(context, handle))
    {
        return 0;
    }
    ClearError(context);
    context->Manager->StopEffect(handle);
    return 1;
}

int32_t LUDOTS_EFK_CALL ludots_effekseer_set_transform(
    void* rawContext,
    int32_t handle,
    float x,
    float y,
    float z,
    float rotationX,
    float rotationY,
    float rotationZ,
    float scaleX,
    float scaleY,
    float scaleZ)
{
    Context* context;
    if (!RequireContext(rawContext, context) || !RequireHandle(context, handle))
    {
        return 0;
    }
    ClearError(context);
    context->Manager->SetLocation(handle, x, y, z);
    context->Manager->SetRotation(handle, rotationX, rotationY, rotationZ);
    context->Manager->SetScale(handle, scaleX, scaleY, scaleZ);
    return 1;
}

int32_t LUDOTS_EFK_CALL ludots_effekseer_set_target(void* rawContext, int32_t handle, float x, float y, float z)
{
    Context* context;
    if (!RequireContext(rawContext, context) || !RequireHandle(context, handle))
    {
        return 0;
    }
    ClearError(context);
    context->Manager->SetTargetLocation(handle, x, y, z);
    return 1;
}

int32_t LUDOTS_EFK_CALL ludots_effekseer_set_color(
    void* rawContext,
    int32_t handle,
    uint8_t red,
    uint8_t green,
    uint8_t blue,
    uint8_t alpha)
{
    Context* context;
    if (!RequireContext(rawContext, context) || !RequireHandle(context, handle))
    {
        return 0;
    }
    ClearError(context);
    context->Manager->SetAllColor(handle, Effekseer::Color(red, green, blue, alpha));
    return 1;
}

int32_t LUDOTS_EFK_CALL ludots_effekseer_set_dynamic_input(void* rawContext, int32_t handle, int32_t index, float value)
{
    Context* context;
    if (!RequireContext(rawContext, context) || !RequireHandle(context, handle))
    {
        return 0;
    }
    if (index < 0 || index >= DynamicInputSlotCount || !std::isfinite(value))
    {
        SetError(context, "Dynamic input index is outside the bridge contract or value is not finite.");
        return 0;
    }
    ClearError(context);
    context->Manager->SetDynamicInput(handle, index, value);
    return 1;
}

int32_t LUDOTS_EFK_CALL ludots_effekseer_set_shown(void* rawContext, int32_t handle, int32_t shown)
{
    Context* context;
    if (!RequireContext(rawContext, context) || !RequireHandle(context, handle))
    {
        return 0;
    }
    ClearError(context);
    context->Manager->SetShown(handle, shown != 0);
    return 1;
}

int32_t LUDOTS_EFK_CALL ludots_effekseer_update(void* rawContext, float deltaSeconds)
{
    Context* context;
    if (!RequireContext(rawContext, context))
    {
        return 0;
    }
    if (!std::isfinite(deltaSeconds) || deltaSeconds < 0.0f)
    {
        SetError(context, "Update delta must be finite and non-negative.");
        return 0;
    }

    ClearError(context);
    context->TimeSeconds += deltaSeconds;
    Effekseer::Manager::UpdateParameter parameter;
    parameter.DeltaFrame = deltaSeconds * 60.0f;
    parameter.UpdateInterval = 0.0f;
    parameter.SyncUpdate = true;
    context->Manager->Update(parameter);
    return 1;
}

int32_t LUDOTS_EFK_CALL ludots_effekseer_draw_perspective(
    void* rawContext,
    float cameraX,
    float cameraY,
    float cameraZ,
    float targetX,
    float targetY,
    float targetZ,
    float upX,
    float upY,
    float upZ,
    float verticalFovRadians,
    float aspect,
    float nearPlane,
    float farPlane)
{
    Context* context;
    if (!RequireContext(rawContext, context))
    {
        return 0;
    }
    if (!(verticalFovRadians > 0.0f) || !(aspect > 0.0f) || !(nearPlane > 0.0f) || !(farPlane > nearPlane))
    {
        SetError(context, "Perspective camera parameters are invalid.");
        return 0;
    }

    ClearError(context);
    const Effekseer::Vector3D camera(cameraX, cameraY, cameraZ);
    const Effekseer::Vector3D target(targetX, targetY, targetZ);
    const Effekseer::Vector3D up(upX, upY, upZ);

    Effekseer::Matrix44 projection;
    projection.PerspectiveFovRH_OpenGL(verticalFovRadians, aspect, nearPlane, farPlane);
    Effekseer::Matrix44 view;
    view.LookAtRH(camera, target, up);

    Effekseer::Manager::LayerParameter layer;
    layer.ViewerPosition = camera;
    context->Manager->SetLayerParameter(0, layer);
    context->Renderer->SetTime(context->TimeSeconds);
    context->Renderer->SetProjectionMatrix(projection);
    context->Renderer->SetCameraMatrix(view);
    context->Renderer->BeginRendering();

    Effekseer::Manager::DrawParameter draw;
    draw.ZNear = 0.0f;
    draw.ZFar = 1.0f;
    draw.CameraPosition = camera;
    draw.CameraFrontDirection = Effekseer::Vector3D::Normal(Effekseer::Vector3D::Sub(draw.CameraFrontDirection, target, camera));
    draw.ViewProjectionMatrix = context->Renderer->GetCameraProjectionMatrix();
    context->Manager->Draw(draw);
    context->Renderer->EndRendering();
    return 1;
}
