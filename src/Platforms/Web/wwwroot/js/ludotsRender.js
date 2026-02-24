// GPU Driven Rendering Logic (Vertex Texture Fetch)

export function updateEntityPositionsInt32(positions, entityCount) {
    const mesh = window.ludots?.entityMesh;
    // We expect the mesh to have userData.posTexture from game.js setup
    if (!mesh || !mesh.userData.posTexture) return;

    const count = entityCount | 0;
    if (count <= 0) {
        return;
    }

    // Required floats for POS (4 floats per entity now: X,Y,Z,W)
    const requiredFloats = count * 4;
    const requiredBytes = requiredFloats * 4;

    // Ensure local Uint8 buffer for MemoryView copy
    // We reuse this buffer to avoid GC
    if (!window._ludotsByteBuffer || window._ludotsByteBuffer.length < requiredBytes) {
        window._ludotsByteBuffer = new Uint8Array(requiredBytes);
        console.log("Ludots: Allocated new Uint8Array buffer of size " + requiredBytes);
    }

    // 1. Copy Data from WASM to Local Buffer
    if (positions && typeof positions.copyTo === 'function') {
        positions.copyTo(window._ludotsByteBuffer);
    } else {
        console.warn("Ludots: positions is not a MemoryView?", positions);
        return;
    }

    // 2. Update Texture Data
    const posTexture = mesh.userData.posTexture;
    const texData = posTexture.image.data; // Float32Array

    // Create Float32 view on the local buffer
    // TODO: We could cache this view if buffer size doesn't change, but it's cheap enough
    const sourceFloats = new Float32Array(window._ludotsByteBuffer.buffer, 0, requiredFloats);
    
    // Direct Bulk Copy (Zero Loop)
    // C# now sends [x, y, z, w] aligned data, so we can just set() it.
    // texData is already RGBA (4 floats per pixel)
    
    // Performance Note: .set() is implemented via memcpy in C++ engine, extremely fast.
    texData.set(sourceFloats);

    // 3. Mark Texture for Upload
    posTexture.needsUpdate = true;
    
    // Update instance count if changed
    if (mesh.geometry.instanceCount !== count) {
        mesh.geometry.instanceCount = count;
    }
}
