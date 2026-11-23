import * as THREE from 'https://cdn.jsdelivr.net/npm/three@0.170.0/build/three.module.js';

const container = document.getElementById('particle-sphere');

if (container) {
    // ---------- Tweakable settings ----------
    const SETTINGS = {
        particleCount: 16000,   // more = denser / more defined surface
        radius: 1.45,           // sphere size (camera z = 4.5)
        bump: 0.085,            // height of the bumps / folds
        bumpSpeed: 0.12,        // how fast the bumps drift
        rimBoost: 2.0,          // how much brighter the edges are than the middle
        centerBrightness: 0.3,  // brightness of dots facing the camera
        backFaceDim: 0.45,      // dims the far side so the middle stays dark
        dotSize: 1.0,           // multiplier on dot size
        haloStrength: 1.0,      // glow around the edge
        rotationSpeed: 0.12
    };

    const scene = new THREE.Scene();
    const camera = new THREE.PerspectiveCamera(45, 1, 0.1, 100);
    camera.position.z = 4.5;

    const renderer = new THREE.WebGLRenderer({ alpha: true, antialias: true });
    renderer.setClearColor(0x000000, 0);
    renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
    renderer.setSize(container.clientWidth, container.clientHeight);
    container.appendChild(renderer.domElement);

    const sphereGroup = new THREE.Group();
    scene.add(sphereGroup);

    // ---------- Geometry: evenly spaced points on a unit sphere ----------
    const count = SETTINGS.particleCount;
    const positions = new Float32Array(count * 3);
    const rand = new Float32Array(count);
    const goldenAngle = Math.PI * (3 - Math.sqrt(5));
    for (let i = 0; i < count; i++) {
        const y = 1 - (i / (count - 1)) * 2;
        const r = Math.sqrt(Math.max(0, 1 - y * y));
        const theta = goldenAngle * i;
        positions[i * 3] = Math.cos(theta) * r;
        positions[i * 3 + 1] = y;
        positions[i * 3 + 2] = Math.sin(theta) * r;
        rand[i] = Math.random();
    }
    const geometry = new THREE.BufferGeometry();
    geometry.setAttribute('position', new THREE.BufferAttribute(positions, 3));
    geometry.setAttribute('aRand', new THREE.BufferAttribute(rand, 1));

    // ---------- Shaders ----------
    const vertexShader = /* glsl */ `
        uniform float uTime;
        uniform float uRadius;
        uniform float uBump;
        uniform float uBumpSpeed;
        uniform float uSizePx;
        uniform float uPixelRatio;
        uniform float uSizeMul;
        uniform float uHalo;
        uniform float uRim;
        uniform float uCenter;
        uniform float uBackDim;

        attribute float aRand;

        varying vec3 vViewDir;     // undisplaced direction in view space (drives colour)
        varying float vIntensity;
        varying float vRim;

        vec3 mod289(vec3 x){ return x - floor(x * (1.0 / 289.0)) * 289.0; }
        vec4 mod289(vec4 x){ return x - floor(x * (1.0 / 289.0)) * 289.0; }
        vec4 permute(vec4 x){ return mod289(((x * 34.0) + 10.0) * x); }
        vec4 taylorInvSqrt(vec4 r){ return 1.79284291400159 - 0.85373472095314 * r; }

        float snoise(vec3 v){
            const vec2 C = vec2(1.0/6.0, 1.0/3.0);
            const vec4 D = vec4(0.0, 0.5, 1.0, 2.0);
            vec3 i  = floor(v + dot(v, C.yyy));
            vec3 x0 = v - i + dot(i, C.xxx);
            vec3 g = step(x0.yzx, x0.xyz);
            vec3 l = 1.0 - g;
            vec3 i1 = min(g.xyz, l.zxy);
            vec3 i2 = max(g.xyz, l.zxy);
            vec3 x1 = x0 - i1 + C.xxx;
            vec3 x2 = x0 - i2 + C.yyy;
            vec3 x3 = x0 - D.yyy;
            i = mod289(i);
            vec4 p = permute(permute(permute(
                      i.z + vec4(0.0, i1.z, i2.z, 1.0))
                    + i.y + vec4(0.0, i1.y, i2.y, 1.0))
                    + i.x + vec4(0.0, i1.x, i2.x, 1.0));
            float n_ = 0.142857142857;
            vec3 ns = n_ * D.wyz - D.xzx;
            vec4 j = p - 49.0 * floor(p * ns.z * ns.z);
            vec4 x_ = floor(j * ns.z);
            vec4 y_ = floor(j - 7.0 * x_);
            vec4 x = x_ * ns.x + ns.yyyy;
            vec4 y = y_ * ns.x + ns.yyyy;
            vec4 h = 1.0 - abs(x) - abs(y);
            vec4 b0 = vec4(x.xy, y.xy);
            vec4 b1 = vec4(x.zw, y.zw);
            vec4 s0 = floor(b0) * 2.0 + 1.0;
            vec4 s1 = floor(b1) * 2.0 + 1.0;
            vec4 sh = -step(h, vec4(0.0));
            vec4 a0 = b0.xzyw + s0.xzyw * sh.xxyy;
            vec4 a1 = b1.xzyw + s1.xzyw * sh.zzww;
            vec3 p0 = vec3(a0.xy, h.x);
            vec3 p1 = vec3(a0.zw, h.y);
            vec3 p2 = vec3(a1.xy, h.z);
            vec3 p3 = vec3(a1.zw, h.w);
            vec4 norm = taylorInvSqrt(vec4(dot(p0,p0), dot(p1,p1), dot(p2,p2), dot(p3,p3)));
            p0 *= norm.x; p1 *= norm.y; p2 *= norm.z; p3 *= norm.w;
            vec4 m = max(0.6 - vec4(dot(x0,x0), dot(x1,x1), dot(x2,x2), dot(x3,x3)), 0.0);
            m = m * m;
            return 42.0 * dot(m * m, vec4(dot(p0,x0), dot(p1,x1), dot(p2,x2), dot(p3,x3)));
        }

        // Big soft lumps + sharper ridged folds = the crinkled look
        float field(vec3 d){
            float t = uTime * uBumpSpeed;
            float lumps = snoise(d * 1.5 + vec3(0.0, t, t * 0.7));
            float folds = 0.5 - abs(snoise(d * 2.8 + vec3(t * 0.8, 3.1, 7.7)));
            return lumps * 0.65 + folds * 0.55;
        }

        vec3 surface(vec3 d){
            return d * (1.0 + uBump * field(d));
        }

        void main(){
            vec3 dir = normalize(position);

            // displaced position
            vec3 p0 = surface(dir);

            // displaced normal via finite differences (this is what gives the ridge contrast)
            vec3 up = abs(dir.y) < 0.99 ? vec3(0.0, 1.0, 0.0) : vec3(1.0, 0.0, 0.0);
            vec3 t1 = normalize(cross(dir, up));
            vec3 t2 = cross(dir, t1);
            float e = 0.02;
            vec3 p1 = surface(normalize(dir + t1 * e));
            vec3 p2 = surface(normalize(dir + t2 * e));
            vec3 n = normalize(cross(p1 - p0, p2 - p0));
            if (dot(n, dir) < 0.0) n = -n;

            vec4 mv = modelViewMatrix * vec4(p0 * uRadius, 1.0);
            gl_Position = projectionMatrix * mv;

            vec3 nView = normalize(normalMatrix * n);
            vec3 toCam = normalize(-mv.xyz);
            float facing = dot(nView, toCam);

            // 0 when facing camera, 1 at silhouette / on steep ridges
            float rim = 1.0 - abs(facing);
            float rimP = pow(clamp(rim, 0.0, 1.0), 2.2);
            vRim = rimP;

            float side = facing > 0.0 ? 1.0 : uBackDim;
            float core = (uCenter + uRim * rimP) * side;
            float halo = (0.08 + 1.0 * rimP) * side;
            vIntensity = mix(core, halo, uHalo) * (0.85 + 0.3 * aRand);

            vViewDir = normalize(normalMatrix * dir);

            float size = uSizePx * uSizeMul * (1.0 + rimP * 0.55) * (0.9 + 0.2 * aRand);
            gl_PointSize = size * uPixelRatio * (4.5 / -mv.z);
        }
    `;

    const fragmentShader = /* glsl */ `
        uniform float uHalo;
        uniform float uAlpha;

        varying vec3 vViewDir;
        varying float vIntensity;
        varying float vRim;

        void addStop(vec2 d, float deg, vec3 col, inout vec3 sum, inout float wsum){
            float a = radians(deg);
            float w = exp(5.0 * (dot(d, vec2(cos(a), sin(a))) - 1.0));
            sum += col * w;
            wsum += w;
        }

        // Colours are fixed to the screen (not the sphere), like the reference
        vec3 palette(vec3 n){
            vec2 flat2 = n.xy;
            float r = length(flat2);
            vec2 d = flat2 / max(r, 1e-4);

            vec3 sum = vec3(0.0);
            float wsum = 0.0;
            addStop(d,  85.0, vec3(1.00, 0.42, 0.02), sum, wsum); // orange (top)
            addStop(d, 130.0, vec3(1.00, 0.38, 0.00), sum, wsum); // orange (top-left)
            addStop(d, 200.0, vec3(0.95, 0.15, 1.00), sum, wsum); // magenta (left-bottom)
            addStop(d, 255.0, vec3(0.50, 0.10, 1.00), sum, wsum); // violet (bottom)
            addStop(d, 315.0, vec3(0.05, 0.55, 1.00), sum, wsum); // blue (bottom-right)
            addStop(d, 358.0, vec3(0.00, 0.90, 1.00), sum, wsum); // cyan (right)
            addStop(d,  38.0, vec3(0.45, 0.25, 1.00), sum, wsum); // violet-blue (top-right)
            vec3 rimColor = sum / wsum;

            vec3 centerBlue = vec3(0.05, 0.38, 1.00);
            return mix(centerBlue, rimColor, smoothstep(0.30, 0.92, r));
        }

        void main(){
            vec2 c = gl_PointCoord - 0.5;
            float d = length(c) * 2.0;
            if (d > 1.0) discard;

            // crisp dot with a hot core, or a very soft blob for the halo layer
            float dot_ = smoothstep(1.0, 0.2, d);
            float soft = pow(1.0 - d, 2.0);
            float a = mix(dot_, soft, uHalo) * uAlpha;

            vec3 col = palette(normalize(vViewDir));
            // edges go slightly whiter-hot, like bloom
            col += vec3(0.12) * vRim;
            col *= vIntensity;

            gl_FragColor = vec4(col, a);
        }
    `;

    function makeMaterial(isHalo) {
        return new THREE.ShaderMaterial({
            vertexShader,
            fragmentShader,
            transparent: true,
            depthWrite: false,
            depthTest: false,
            blending: THREE.AdditiveBlending,
            uniforms: {
                uTime: { value: 0 },
                uRadius: { value: SETTINGS.radius },
                uBump: { value: SETTINGS.bump },
                uBumpSpeed: { value: SETTINGS.bumpSpeed },
                uSizePx: { value: 2.2 },
                uPixelRatio: { value: renderer.getPixelRatio() },
                uSizeMul: { value: isHalo ? 7.0 : SETTINGS.dotSize },
                uHalo: { value: isHalo ? 1 : 0 },
                uAlpha: { value: isHalo ? 0.05 * SETTINGS.haloStrength : 1.0 },
                uRim: { value: SETTINGS.rimBoost },
                uCenter: { value: SETTINGS.centerBrightness },
                uBackDim: { value: SETTINGS.backFaceDim }
            }
        });
    }

    const haloMaterial = makeMaterial(true);
    const coreMaterial = makeMaterial(false);

    const halo = new THREE.Points(geometry, haloMaterial);
    const core = new THREE.Points(geometry, coreMaterial);
    halo.frustumCulled = false;
    core.frustumCulled = false;
    sphereGroup.add(halo);
    sphereGroup.add(core);

    // ---------- Animation ----------
    const clock = new THREE.Clock();
    let animationId = null;

    function animate() {
        animationId = requestAnimationFrame(animate);
        const time = clock.getElapsedTime();

        haloMaterial.uniforms.uTime.value = time;
        coreMaterial.uniforms.uTime.value = time;

        sphereGroup.rotation.y = time * SETTINGS.rotationSpeed;
        sphereGroup.rotation.x = Math.sin(time * 0.3) * 0.08;

        renderer.render(scene, camera);
    }

    function resize() {
        const width = container.clientWidth, height = container.clientHeight;
        if (!width || !height) return;
        camera.aspect = width / height;
        camera.updateProjectionMatrix();
        renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
        renderer.setSize(width, height);

        // keep dots proportionally sized on any container size
        const sizePx = Math.min(3.6, Math.max(1.5, Math.min(width, height) * 0.0032));
        [haloMaterial, coreMaterial].forEach((m) => {
            m.uniforms.uSizePx.value = sizePx;
            m.uniforms.uPixelRatio.value = renderer.getPixelRatio();
        });
    }

    const resizeObserver = new ResizeObserver(resize);
    resizeObserver.observe(container);
    resize();
    animate();

    document.addEventListener('visibilitychange', () => {
        if (document.hidden) {
            cancelAnimationFrame(animationId);
            animationId = null;
        } else if (animationId === null) {
            animate();
        }
    });
}