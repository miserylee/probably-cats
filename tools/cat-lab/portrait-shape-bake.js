/* Project the neutral carving and its five independent gene responses into the inventory camera. */
(() => {
  "use strict";

  function create(T, width, height, age, framing) {
    const scale = 2;
    const bufferWidth = width * scale, bufferHeight = height * scale;
    const renderer = new T.WebGLRenderer({ antialias: false, alpha: true, premultipliedAlpha: false });
    renderer.setClearColor(0, 0);
    const target = new T.WebGLRenderTarget(bufferWidth, bufferHeight);
    const halfHeight = framing.iconHeight * .5, halfWidth = halfHeight * width / height;
    const camera = new T.OrthographicCamera(-halfWidth, halfWidth, halfHeight, -halfHeight, .1, 40);
    const kitten = age === "kitten";
    const angle = framing.facing * (Math.PI / 2 - (kitten ? CatModel.kittenHeadTurn : 0));
    const [x, y, z] = framing.iconTarget;
    camera.position.set(x + Math.sin(angle) * 10, y + .35, z + Math.cos(angle) * 10);
    camera.lookAt(x, y, z);
    camera.updateMatrixWorld(true);

    const scene = new T.Scene();
    const material = new T.ShaderMaterial({
      side: T.DoubleSide,
      blending: T.NoBlending,
      vertexShader: `attribute vec4 response;
        varying vec4 movement;
        void main() {
          movement = response;
          gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0);
        }`,
      fragmentShader: `varying vec4 movement;
        void main() { gl_FragColor = (vec4(128.0) + movement * 6.0) / 255.0; }`
    });

    function build(genes) {
      const root = CatModel.build(T, { ...CatModel.defaults, age, ...genes });
      root.updateMatrixWorld(true);
      const meshes = [];
      root.traverse(mesh => { if (mesh.isMesh) meshes.push(mesh); });
      const materials = [...new Set(meshes.map(mesh => mesh.material))];
      return { root, meshes, materials };
    }

    function dispose(model) {
      model.meshes.forEach(mesh => mesh.geometry.dispose());
      model.materials.forEach(material => material.dispose());
    }

    function connect(source, lower, upper, trait) {
      if (source.meshes.length !== lower.meshes.length || source.meshes.length !== upper.meshes.length)
        throw new Error(`Mesh count changed for ${age}/${trait}`);

      const a = new T.Vector3(), b = new T.Vector3();
      for (let index = 0; index < source.meshes.length; index++) {
        const mesh = source.meshes[index], low = lower.meshes[index], high = upper.meshes[index];
        const geometry = mesh.geometry;
        const reference = geometry.getAttribute("position");
        const minPositions = low.geometry.getAttribute("position"), maxPositions = high.geometry.getAttribute("position");
        if (mesh.name !== low.name || mesh.name !== high.name ||
            reference.count !== minPositions.count || reference.count !== maxPositions.count)
          throw new Error(`Vertex correspondence changed for ${age}/${trait}/${mesh.name}`);
        const output = new Float32Array(reference.count * 4);
        for (let i = 0; i < reference.count; i++) {
          a.fromBufferAttribute(reference, i).applyMatrix4(mesh.matrixWorld).project(camera);
          b.fromBufferAttribute(minPositions, i).applyMatrix4(low.matrixWorld).project(camera);
          output[4 * i] = (b.x - a.x) * width * .5;
          output[4 * i + 1] = (b.y - a.y) * height * .5;
          b.fromBufferAttribute(maxPositions, i).applyMatrix4(high.matrixWorld).project(camera);
          output[4 * i + 2] = (b.x - a.x) * width * .5;
          output[4 * i + 3] = (b.y - a.y) * height * .5;
        }
        geometry.setAttribute("response", new T.BufferAttribute(output, 4));
        mesh.material = material;
      }
    }

    function raster(source) {
      scene.add(source.root);
      renderer.setRenderTarget(target);
      renderer.render(scene, camera);
      const raw = new Uint8Array(bufferWidth * bufferHeight * 4);
      renderer.readRenderTargetPixels(target, 0, 0, bufferWidth, bufferHeight, raw);
      renderer.setRenderTarget(null);
      scene.remove(source.root);
      let result = "";
      for (let i = 0; i < raw.length; i += 8192) result += String.fromCharCode(...raw.subarray(i, i + 8192));
      return btoa(result);
    }

    return {
      capture(genes, trait) {
        const base = build(genes), low = build({ ...genes, [trait]: .2 }), high = build({ ...genes, [trait]: .8 });
        try {
          connect(base, low, high, trait);
          return raster(base);
        } finally {
          [base, low, high].forEach(dispose);
        }
      },
      dispose() { material.dispose(); target.dispose(); renderer.dispose(); renderer.forceContextLoss(); }
    };
  }
  window.createPortraitShapeBaker = create;
})();
