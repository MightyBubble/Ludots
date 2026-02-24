window.ludots = {
    scene: null,
    camera: null,
    renderer: null,
    terrainMesh: null,
    entityMesh: null,
    dotnetRef: null,
    lastTime: 0,
    
    init: function(containerId) {
        console.log("Ludots: Initializing Three.js...");
        const container = document.getElementById(containerId);
        if (!container) {
            console.error("Ludots: Container not found!");
            return;
        }
        
        // Scene
        this.scene = new THREE.Scene();
        this.scene.background = new THREE.Color(0xD3D3D3); // LightGray
        
        // Camera
        this.camera = new THREE.PerspectiveCamera(45, container.clientWidth / container.clientHeight, 0.1, 2000);
        this.camera.position.set(10, 10, 10); // Raylib debug position
        this.camera.lookAt(0, 0, 0);
        
        // Renderer
        this.renderer = new THREE.WebGLRenderer({ antialias: true });
        this.renderer.setSize(container.clientWidth, container.clientHeight);
        container.appendChild(this.renderer.domElement);
        
        // Helpers
        const axesHelper = new THREE.AxesHelper(5);
        this.scene.add(axesHelper);
        const gridHelper = new THREE.GridHelper(20, 20); // 20x20 grid, 1.0 step
        gridHelper.position.set(0, 0, 0);
        this.scene.add(gridHelper);

        // Lights
        const light = new THREE.DirectionalLight(0xffffff, 1);
        light.position.set(10, 10, 10).normalize();
        this.scene.add(light);
        this.scene.add(new THREE.AmbientLight(0x606060)); // Brighter ambient
        
        // Setup Terrain
        this.setupTerrain();
        
        // Setup Entities
        this.setupEntities();
        
        // Resize handler
        window.addEventListener('resize', () => {
            this.camera.aspect = container.clientWidth / container.clientHeight;
            this.camera.updateProjectionMatrix();
            this.renderer.setSize(container.clientWidth, container.clientHeight);
        });
        
        this.animate(0);
        console.log("Ludots: Initialization Complete.");
    },

    startGameLoop: function(dotnetRef) {
        console.log("Ludots: Starting Game Loop...");
        this.dotnetRef = dotnetRef;
        
        // Add FPS Stats
        try {
            this.stats = new Stats();
            this.stats.showPanel(0); 
            this.stats.dom.style.position = 'fixed'; // Use fixed instead of absolute
            this.stats.dom.style.top = '10px';
            this.stats.dom.style.left = '10px';
            this.stats.dom.style.zIndex = '99999'; // Higher z-index
            document.body.appendChild(this.stats.dom);
            console.log("Ludots: Stats added to DOM");
        } catch (e) {
            console.error("Ludots: Failed to add Stats", e);
        }
    },
    
    setupTerrain: function() {
        // Hide Terrain for now (focus on entities)
    },

    setupEntities: function() {
        const count = 20000; 
        
        // 1. Create InstancedBufferGeometry
        const baseGeometry = new THREE.BoxGeometry(0.2, 0.2, 0.2);
        this.entityGeometry = new THREE.InstancedBufferGeometry();
        this.entityGeometry.index = baseGeometry.index;
        this.entityGeometry.attributes.position = baseGeometry.attributes.position;
        this.entityGeometry.attributes.normal = baseGeometry.attributes.normal;
        this.entityGeometry.attributes.uv = baseGeometry.attributes.uv;

        // 2. Generate instance indices (0, 1, 2... N)
        const instances = new Float32Array(count);
        for (let i = 0; i < count; i++) instances[i] = i;
        this.entityGeometry.setAttribute('instanceIndex', new THREE.InstancedBufferAttribute(instances, 1));

        // 3. Create DataTexture for Positions
        // Texture size: Using a square texture is better for compatibility
        // 20000 pixels fits in 142x142, let's use 256x256 for power of two or just a wider texture.
        // MAX_TEXTURE_SIZE is usually 4096 or 8192, so 20000x1 is fine on desktop but maybe risky on some mobile.
        // Let's stick to N x 1 for simplicity first, but optimize if needed.
        // Re-check warning: "MAX_TEXTURE_SIZE" usually refers to width or height.
        // If 20000 > MAX_TEXTURE_SIZE (e.g. 16384), it will be clamped.
        // Let's use a 2D texture approach: 512 width
        const texWidth = 512;
        const texHeight = Math.ceil(count / texWidth);
        
        const data = new Float32Array(texWidth * texHeight * 4); // RGBA
        this.posTexture = new THREE.DataTexture(data, texWidth, texHeight, THREE.RGBAFormat, THREE.FloatType);
        this.posTexture.needsUpdate = true;

        // 4. Custom Shader Material
        const material = new THREE.ShaderMaterial({
            uniforms: {
                posTexture: { value: this.posTexture },
                texSize: { value: new THREE.Vector2(texWidth, texHeight) },
                color: { value: new THREE.Color(0x0000FF) }
            },
            vertexShader: `
                uniform sampler2D posTexture;
                uniform vec2 texSize;
                attribute float instanceIndex;
                varying vec2 vUv;
                varying vec3 vNormal;

                void main() {
                    vUv = uv;
                    vNormal = normal;

                    // Calculate 2D UV coordinate for texture fetch based on instanceIndex
                    // index -> (x, y)
                    float x = mod(instanceIndex, texSize.x);
                    float y = floor(instanceIndex / texSize.x);
                    
                    // Center sampling
                    vec2 fetchUv = vec2((x + 0.5) / texSize.x, (y + 0.5) / texSize.y);

                    // Fetch position from texture
                    vec4 posData = texture2D(posTexture, fetchUv);
                    vec3 instancePos = posData.xyz;

                    // Standard transformation
                    vec3 transformed = position + instancePos;
                    gl_Position = projectionMatrix * modelViewMatrix * vec4(transformed, 1.0);
                }
            `,
            fragmentShader: `
                uniform vec3 color;
                varying vec3 vNormal;
                
                void main() {
                    // Simple lighting
                    vec3 light = normalize(vec3(1.0, 1.0, 1.0));
                    float diff = max(dot(vNormal, light), 0.2);
                    gl_FragColor = vec4(color * diff, 1.0);
                }
            `
        });

        this.entityMesh = new THREE.Mesh(this.entityGeometry, material);
        this.entityMesh.frustumCulled = false; // Always render
        this.scene.add(this.entityMesh);
        
        // Expose texture info for update
        this.entityMesh.userData = { 
            posTexture: this.posTexture,
            texWidth: texWidth,
            texHeight: texHeight
        };
    },

    updateTerrainHeight: function(heightDataPtr) {
        // TODO: Receive byte array from C# and update Y scale of instances
    },
    
    animate: function(time) {
        if (this.stats) this.stats.begin();
        requestAnimationFrame((t) => this.animate(t));
        
        const dt = time - this.lastTime;
        this.lastTime = time;

        // Call C# Game Loop
        if (this.dotnetRef && dt > 0) {
            try {
                this.dotnetRef.invokeMethod('GameLoop', dt);
            } catch (e) {
                console.error(e);
            }
        }

        if (this.renderer && this.scene && this.camera) {
            this.renderer.render(this.scene, this.camera);
        }
        if (this.stats) this.stats.end();
    },
    
    updateEntityPositions: function(positions) {
        // Kept for interface compatibility, but we use updateEntityPositionsInt32 for texture update
    }
};
