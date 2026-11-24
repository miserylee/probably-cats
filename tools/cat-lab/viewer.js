/* Fixed inventory photography and interactive model inspection. */
(function () {
  "use strict";

  const framing = {
    adult: { target: [.12, 1.56, 0], iconTarget: [.12, 1.56, .02], height: 3.55, width: 3.15, iconHeight: 3.96, facing: -1 },
    kitten: { target: [.04, .59, 0], iconTarget: [.04, .56, 0], height: 1.58, width: 3.80, iconHeight: 1.37, facing: -1 }
  };
  const stagePlaces = {
    adult: {x:-.87, z:-.40, yaw:Math.PI/2},
    kitten: {x:1.12, z:.36, yaw:.86}
  };

  function releaseTree(root) {
    const resources = new Set();
    root.traverse(function (node) {
      if (node.geometry) resources.add(node.geometry);
      if (!node.material) return;
      const materials = Array.isArray(node.material) ? node.material : [node.material];
      for (const material of materials) {
        for (const value of Object.values(material)) {
          if (value && value.isTexture) resources.add(value);
        }
        resources.add(material);
      }
    });
    resources.forEach(function (resource) { resource.dispose(); });
  }

  function makeShadow(THREE) {
    const textureCanvas = document.createElement("canvas");
    textureCanvas.width = textureCanvas.height = 128;
    const context = textureCanvas.getContext("2d");
    const gradient = context.createRadialGradient(64, 64, 0, 64, 64, 64);
    gradient.addColorStop(0, "rgba(78,61,47,0.20)");
    gradient.addColorStop(0.46, "rgba(78,61,47,0.11)");
    gradient.addColorStop(1, "rgba(78,61,47,0)");
    context.fillStyle = gradient;
    context.fillRect(0, 0, 128, 128);
    const tex = new THREE.CanvasTexture(textureCanvas);
    tex.colorSpace = THREE.SRGBColorSpace;
    const floor = new THREE.Group();
    const ambient = new THREE.Mesh(new THREE.PlaneGeometry(1, 1),
      new THREE.MeshBasicMaterial({ map: tex, transparent: true, depthWrite: false }));
    ambient.rotation.x = -Math.PI / 2;
    ambient.position.y = -0.015;
    ambient.renderOrder = -1;
    floor.add(ambient);
    const contact = ambient.clone();
    contact.material = ambient.material.clone();
    contact.scale.set(1.15, .80, 1);
    contact.position.y = -.012;
    floor.add(contact);
    const toes = new THREE.Group();
    floor.add(toes);
    function fitPaws(model) {
      toes.clear();
      toes.rotation.copy(model.rotation);
      for (const mesh of model.children) {
        if (mesh.name !== "foreleg" && mesh.name !== "haunch") continue;
        const vertices = mesh.geometry.getAttribute("position");
        let xmin=Infinity, xmax=-Infinity, zmin=Infinity, zmax=-Infinity;
        for (let i=0;i<vertices.count;i++) {
          if (vertices.getY(i)>.10) continue;
          const x=vertices.getX(i), z=vertices.getZ(i);
          xmin=Math.min(xmin,x); xmax=Math.max(xmax,x);
          zmin=Math.min(zmin,z); zmax=Math.max(zmax,z);
        }
        if (xmin>xmax) continue;
        const footprint = contact.clone();
        footprint.position.set((xmin+xmax)/2,-.005,(zmin+zmax)/2);
        footprint.scale.set((xmax-xmin)*1.60,(zmax-zmin)*1.45,1);
        toes.add(footprint);
      }
    }
    return { floor, ambient, contact, fitPaws };
  }

  window.createCatViewer = function ({ canvas, iconCanvas, onViewChange, inventoryOnly = false }) {
    const T = window.THREE;
    const renderer = new T.WebGLRenderer({
      canvas, antialias: true, alpha: true, premultipliedAlpha: false
    });
    renderer.outputColorSpace = T.SRGBColorSpace;
    renderer.setClearColor(0x000000, 0);
    renderer.shadowMap.enabled = !inventoryOnly;
    renderer.shadowMap.autoUpdate = false;
    renderer.shadowMap.type = T.PCFSoftShadowMap;
    const scene = new T.Scene();
    scene.add(new T.HemisphereLight(0xf1f4ff, 0xc2b7ac, 1.35));
    const key = new T.DirectionalLight(0xfff4e1, 2.12);
    key.position.set(-4.6, 6.5, 4.7);
    key.castShadow = true;
    key.shadow.mapSize.set(1024, 1024);
    Object.assign(key.shadow.camera, { left: -4, right: 4, top: 4, bottom: -4, near: 0.1, far: 18 });
    key.shadow.camera.updateProjectionMatrix();
    key.shadow.normalBias = 0.014;
    key.shadow.bias = -0.00035;
    scene.add(key);
    const rim = new T.DirectionalLight(0xffe6c7, 1.20);
    rim.position.set(3, 4, -3);
    scene.add(rim);
    const fill = new T.DirectionalLight(0xdfedf7, .48);
    fill.position.set(4, 1.5, 4);
    scene.add(fill);
    const { floor, ambient, contact, fitPaws } = makeShadow(T);
    const activeSeat = new T.Group(), otherSeat = new T.Group();
    const otherShadow = makeShadow(T);
    activeSeat.add(floor);
    otherSeat.add(otherShadow.floor);
    scene.add(activeSeat, otherSeat);
    const ground = new T.Mesh(new T.PlaneGeometry(80, 80),
      new T.ShadowMaterial({opacity:.055, transparent:true, depthWrite:false}));
    ground.rotation.x = -Math.PI/2;
    ground.position.y = -.024;
    ground.receiveShadow = true;
    ground.visible = otherSeat.visible = false;
    scene.add(ground);
    const groupCamera = new T.PerspectiveCamera(24, 1, .1, 50);
    let companion = null, referenceKey = "", comparing = false;
    const mainCamera = new T.OrthographicCamera(-1, 1, 1, -1, 0.1, 40);
    const iconCamera = mainCamera.clone();
    const iconContext = iconCanvas.getContext("2d");
    const stamp = document.createElement("canvas");
    const stampContext = stamp.getContext("2d");
    const iconTarget = new T.WebGLRenderTarget(1, 1);
    iconTarget.texture.colorSpace = T.SRGBColorSpace;
    const clay = new T.MeshStandardMaterial({ color: 0xb5b1aa, roughness: 0.88 });

    let model = null, age = "adult", view = "fixed", yaw = 0;
    let materialMode = "coat", modelParts = [];
    let frame = 0, iconDirty = true, destroyed = false;
    let drag = null;

    function angle(preset) {
      const front = Math.PI/2 - (age === "kitten" ? window.CatModel.kittenHeadTurn : 0);
      const radians = preset === "front" ? front : preset === "side" ? front-Math.PI/2 : age === "kitten" ? .84 : 1.04;
      return framing[age].facing * radians;
    }

    function placeCamera(camera, aspect, height, azimuth, elevation, target=framing[age].target) {
      const [x, y, z] = target;
      camera.top = height * 0.5;
      camera.bottom = -camera.top;
      camera.right = camera.top * aspect;
      camera.left = -camera.right;
      camera.position.set(x + Math.sin(azimuth)*10, y + elevation*10, z + Math.cos(azimuth)*10);
      camera.lookAt(x, y, z);
      camera.updateProjectionMatrix();
    }

    function drawIcon() {
      const ratio = Math.min(window.devicePixelRatio || 1, 2);
      const w = Math.round(iconCanvas.clientWidth * ratio);
      const h = Math.round(iconCanvas.clientHeight * ratio);
      if (!w || !h) return;
      if (iconCanvas.width !== w || iconCanvas.height !== h) {
        iconCanvas.width = w;
        iconCanvas.height = h;
      }
      const cols = 2;
      const rows = age === "adult" ? 4 : 1;
      const cell = Math.max(1, Math.floor(Math.min((w-2)/cols, (h-2)/rows)));
      const gw = cols*cell, gh = rows*cell;
      const left = Math.floor((w-gw)/2), top = Math.floor((h-gh)/2);
      iconContext.clearRect(0, 0, w, h);
      iconContext.fillStyle = "rgba(114,129,114,0.055)";
      iconContext.fillRect(left, top, gw, gh);
      iconContext.strokeStyle = "rgba(95,113,100,0.29)";
      iconContext.lineWidth = Math.max(1, Math.round(ratio));
      iconContext.beginPath();
      for (let x=0; x<=cols; x++) {
        iconContext.moveTo(left+x*cell, top);
        iconContext.lineTo(left+x*cell, top+gh);
      }
      for (let y=0; y<=rows; y++) {
        iconContext.moveTo(left, top+y*cell);
        iconContext.lineTo(left+gw, top+y*cell);
      }
      iconContext.stroke();
      // Fixed-size cells preserve the inventory sampling at any panel size.
      const nativeCell = 64;
      const sw = nativeCell * cols, sh = nativeCell * rows;
      iconTarget.setSize(sw, sh);
      const spec = framing[age];
      placeCamera(iconCamera, sw/sh, spec.iconHeight, angle("front"), .035, spec.iconTarget);
      // Inventory photography always uses the selected cat in its calibrated
      // local coordinates, regardless of its placement on the shared stage.
      const onStage = activeSeat.position.clone(), turn = activeSeat.rotation.y;
      activeSeat.position.set(0,0,0);
      activeSeat.rotation.y = 0;
      otherSeat.visible = ground.visible = floor.visible = false;
      renderer.setRenderTarget(iconTarget);
      renderer.render(scene, iconCamera);
      const buffer = new Uint8Array(sw*sh*4);
      renderer.readRenderTargetPixels(iconTarget, 0, 0, sw, sh, buffer);
      renderer.setRenderTarget(null);
      floor.visible = true;
      otherSeat.visible = ground.visible = comparing;
      activeSeat.position.copy(onStage);
      activeSeat.rotation.y = turn;
      stamp.width = sw; stamp.height = sh;
      const image = stampContext.createImageData(sw, sh);
      for (let row=0; row<sh; row++) {
        const src = (sh-row-1)*sw*4;
        image.data.set(buffer.subarray(src, src+sw*4), row*sw*4);
      }
      stampContext.putImageData(image, 0, 0);
      iconContext.imageSmoothingEnabled = gw < sw;
      iconContext.drawImage(stamp, left, top, gw, gh);
    }

    function paint() {
      frame = 0;
      if (!model || destroyed || renderer.getContext().isContextLost()) return;
      if (inventoryOnly) {
        if (iconDirty) {
          drawIcon();
          iconDirty = false;
        }
        return;
      }
      const w = canvas.clientWidth, h = canvas.clientHeight;
      if (!w || !h) return;
      const ratio = Math.min(window.devicePixelRatio || 1, 2);
      if (renderer.getPixelRatio() !== ratio) renderer.setPixelRatio(ratio);
      if (canvas.width !== Math.floor(w*ratio) || canvas.height !== Math.floor(h*ratio)) {
        renderer.setSize(w, h, false);
      }
      const spec = framing[age];
      let camera = mainCamera;
      if (comparing) {
        camera = groupCamera;
        // End-on angles bring a cat an extra body length toward the lens.
        const orbitRoom = 1 + .075 * Math.sin(yaw) ** 2;
        const height = Math.max(3.97, 5.10*h/w) * orbitRoom;
        const distance = height/(2*Math.tan(T.MathUtils.degToRad(camera.fov/2)));
        camera.aspect = w/h;
        const elevation = view === "front" ? .055 : .14;
        camera.position.set(.52+Math.sin(yaw)*distance, 1.36+elevation*distance, Math.cos(yaw)*distance);
        camera.lookAt(.52, 1.36, 0);
        camera.updateProjectionMatrix();
      } else {
        placeCamera(camera, w/h, Math.max(spec.height, spec.width*h/w), yaw, .035);
      }
      if (materialMode === "clay") {
        for (const [mesh] of modelParts) {
          mesh.material = clay;
          if (mesh.userData.coatOnly) mesh.visible = false;
        }
        try {
          renderer.render(scene, camera);
        } finally {
          for (const [mesh, coat, visible] of modelParts) {
            mesh.material = coat;
            mesh.visible = visible;
          }
        }
      } else {
        renderer.render(scene, camera);
      }
      if (iconDirty) {
        drawIcon();
        iconDirty = false;
      }
    }

    function schedule() {
      if (!frame && !destroyed) frame = requestAnimationFrame(paint);
    }

    function setView(preset) {
      if (!["fixed", "front", "side"].includes(preset)) return;
      view = preset; yaw = stageAngle(view);
      if (onViewChange) onViewChange(view);
      schedule();
    }

    function rotate(delta) {
      if (!Number.isFinite(delta)) return;
      yaw += delta;
      if (view !== "free") {
        view = "free";
        if (onViewChange) onViewChange(view);
      }
      schedule();
    }

    function stageAngle(preset) {
      if (!comparing) return angle(preset);
      return preset === "fixed" ? 0 : angle(preset) + stagePlaces[age].yaw;
    }

    function applyPlacement(seat, stage) {
      if (!comparing) {
        seat.position.set(0,0,0);
        seat.rotation.y = 0;
      } else {
        const place = stagePlaces[stage];
        seat.position.set(place.x,0,place.z);
        seat.rotation.y = place.yaw;
      }
    }

    function setupModel(next) {
      next.traverse(function (node) {
        if (node.isMesh && !node.material.transparent) {
          node.castShadow = !node.userData.skipCastShadow;
          node.receiveShadow = true;
        }
      });
    }

    function setupContact(shadow, next, stage) {
      shadow.fitPaws(next);
      shadow.ambient.scale.set(stage === "adult" ? 2.6 : 3.3, stage === "adult" ? 1.85 : 1.3, 1);
      shadow.contact.scale.set(stage === "adult" ? 1.65 : 2.2, stage === "adult" ? 1.02 : .8, 1);
    }

    function update(params, reference = null) {
      if (destroyed) return;
      const next = window.CatModel.build(T, params);
      setupModel(next);
      if (model) { activeSeat.remove(model); releaseTree(model); }
      const nextAge = params.age === "kitten" ? "kitten" : "adult";
      const changedView = comparing !== Boolean(reference);
      const changedAge = nextAge !== age;
      age = nextAge; model = next; comparing = Boolean(reference);
      activeSeat.add(model);
      modelParts = [];
      model.traverse(node => { if(node.isMesh) modelParts.push([node,node.material,node.visible]); });
      setupContact({fitPaws, ambient, contact}, model, age);
      applyPlacement(activeSeat, age);
      otherSeat.visible = ground.visible = comparing;
      const key = reference ? JSON.stringify(reference) : "";
      if (key !== referenceKey) {
        if (companion) { otherSeat.remove(companion); releaseTree(companion); companion = null; }
        if (reference) {
          companion = window.CatModel.build(T,reference);
          setupModel(companion);
          otherSeat.add(companion);
          setupContact(otherShadow,companion,reference.age);
          applyPlacement(otherSeat,reference.age);
        }
        referenceKey = key;
      }
      if (changedView || (changedAge && !comparing && view === "free")) setView("fixed");
      if (view !== "free") yaw = stageAngle(view);
      renderer.shadowMap.needsUpdate = true;
      iconDirty = true;
      schedule();
    }

    function pointerDown(e) {
      if (e.button !== 0 || drag) return;
      drag = { x: e.clientX, id: e.pointerId };
      canvas.setPointerCapture(e.pointerId);
    }
    function pointerMove(e) {
      if (!drag || drag.id !== e.pointerId) return;
      const diff = drag.x - e.clientX;
      if (!diff) return;
      drag.x = e.clientX;
      rotate(diff * Math.PI * 1.35 / Math.max(120, canvas.clientWidth));
    }
    function pointerEnd(e) { if (drag && drag.id === e.pointerId) drag = null; }
    function resize() { iconDirty = true; schedule(); }
    function restore() { renderer.shadowMap.needsUpdate = true; resize(); }
    const observer = new ResizeObserver(resize);
    observer.observe(canvas); observer.observe(iconCanvas);
    canvas.addEventListener("pointerdown", pointerDown);
    canvas.addEventListener("pointermove", pointerMove);
    canvas.addEventListener("lostpointercapture", pointerEnd);
    canvas.addEventListener("webglcontextrestored", restore);
    window.addEventListener("resize", resize);
    return {
      update, setView, rotate, resetView: () => setView("fixed"),
      getCameraState: () => ({view,yaw,materialMode}),
      setMaterialMode(mode) {
        if (mode !== "coat" && mode !== "clay") return;
        materialMode = mode;
        schedule();
      },
      destroy() {
        destroyed = true;
        cancelAnimationFrame(frame);
        observer.disconnect();
        canvas.removeEventListener("pointerdown", pointerDown);
        canvas.removeEventListener("pointermove", pointerMove);
        canvas.removeEventListener("lostpointercapture", pointerEnd);
        canvas.removeEventListener("webglcontextrestored", restore);
        window.removeEventListener("resize", resize);
        releaseTree(scene);
        iconTarget.dispose();
        clay.dispose();
        key.shadow.dispose();
        renderer.dispose();
      }
    };
  };
})();
