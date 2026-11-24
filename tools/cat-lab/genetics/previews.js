(function () {
  "use strict";

  const cacheLimit = 128;
  let instance = null;

  function afterPaint() {
    return new Promise(resolve => requestAnimationFrame(resolve));
  }

  async function createRenderer() {
    const model = window.CatModel;
    await model.prepare(window.THREE);

    const host = document.createElement("div");
    host.setAttribute("aria-hidden", "true");
    Object.assign(host.style, {
      position: "fixed",
      left: "-1000px",
      top: "0",
      visibility: "hidden",
      pointerEvents: "none"
    });
    const canvas = document.createElement("canvas");
    canvas.style.width = "1px";
    canvas.style.height = "1px";
    const iconCanvas = document.createElement("canvas");
    iconCanvas.style.width = "82px";
    iconCanvas.style.height = "162px";
    host.append(canvas, iconCanvas);
    document.body.append(host);

    let viewer;
    try {
      viewer = window.createCatViewer({ canvas, iconCanvas, inventoryOnly: true });
    } catch (error) {
      host.remove();
      model.dispose();
      throw error;
    }

    const cache = new Map();
    const queued = new Map();
    let queue = Promise.resolve();
    let disposed = false;

    function remember(key, url) {
      cache.delete(key);
      cache.set(key, url);
      if (cache.size > cacheLimit) cache.delete(cache.keys().next().value);
    }

    function ensureReady() {
      if (disposed) throw new Error("格内预览已关闭");
    }

    async function image(cat) {
      ensureReady();
      const appearance = cat.phenotype?.appearance;
      if (!appearance) throw new TypeError("这只猫缺少外观表达数据");
      const genes = { ...model.defaults };
      for (const field of Object.keys(genes)) {
        if (appearance[field] != null) genes[field] = appearance[field];
      }
      // Study nodes depict the grown cat in the approved 2 × 4 grid.
      genes.age = "adult";
      const key = JSON.stringify(genes);
      if (cache.has(key)) {
        const url = cache.get(key);
        remember(key, url);
        return url;
      }
      if (queued.has(key)) return queued.get(key);

      const task = queue.then(async () => {
        ensureReady();
        viewer.update(genes);
        // The viewer submits its animation-frame callback before ours.
        await afterPaint();
        ensureReady();
        const url = iconCanvas.toDataURL();
        remember(key, url);
        return url;
      }).finally(() => queued.delete(key));
      queued.set(key, task);
      queue = task.catch(() => {});
      return task;
    }

    return {
      image,
      dispose() {
        if (disposed) return;
        disposed = true;
        cache.clear();
        queued.clear();
        viewer.destroy();
        model.dispose();
        host.remove();
        instance = null;
      }
    };
  }

  window.CatPedigreePreviews = {
    create() {
      instance ??= createRenderer().catch(error => {
        instance = null;
        throw error;
      });
      return instance;
    }
  };
})();
