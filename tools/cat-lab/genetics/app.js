(() => {
  "use strict";
  const get = id => document.getElementById(id);
  const study = window.CatGeneticsStudy;
  const cats = new Map((study?.cats ?? []).map(cat => [cat.id, cat]));
  const mainCats = (study?.lineage ?? []).map(id => cats.get(id));
  const partners = mainCats.slice(0, -1).map((cat, generation) => {
    const child = mainCats[generation + 1];
    if (!cat || !child) return null;
    const id = child.motherId === cat.id ? child.fatherId : child.fatherId === cat.id ? child.motherId : null;
    return cats.get(id);
  });
  if (!mainCats.length || mainCats.some((cat, index) => cat?.generation !== index) || partners.some(cat => !cat)) {
    get("study-error").hidden = false;
    return;
  }
  const finalGeneration = mainCats.length - 1;
  const graph = get("study");
  const world = get("world");
  const viewport = get("viewport");
  const layer = get("nodes");
  const edges = get("edges");
  const detail = get("inspector");
  const panel = get("detail-panel");
  const number = get("generation-number");
  const timeline = get("timeline");
  const cardWidth = 104;
  const cardHeight = 154;
  const step = 156;
  const start = 40;
  const mateTop = 90;
  const vineTop = 313;
  const worldHeight = vineTop + cardHeight + 50;
  const worldWidth = start * 2 + finalGeneration * step + cardWidth;
  const positions = new Map();
  const records = [];
  let x = 0, y = 0, zoom = 1;
  let current = 0;
  let fitted = false;
  let editingGeneration = false;
  let dragging = null;
  let suppressClickUntil = 0;
  let selectedCat = null;
  let inspectedId = null;
  let inspectionMode = "";
  let hoverTimer = 0;
  let paintTimer = 0;
  let previewJob = null;
  let previewBusy = false;
  let previewFailed = false;

  const groups = ["A", "B", "C", "D", "E", "F"];
  const fur = {hairless: "无毛", short: "短毛", long: "长毛"};
  const markings = {
    snowpaws: "踏雪寻梅", inktail: "雪里拖枪", inkseal: "挂印拖枪", none: "普通", gloves: "白手套", tuxedo: "乌云盖雪", bib: "白围脖", point: "重点色",
    van: "梵花", harlequin: "高白斑", spotted: "斑点虎斑"
  };
  const coats = ["原画三花", "纯色", "渐层", "虎斑", "三花", "随机拼色", "烟色", "银渐层", "银壳色"];
  const colors = {
    ginger: "暖橘", cream: "奶油", chalk: "象牙白", fawn: "浅鹿棕", cinnamon: "肉桂",
    chocolate: "巧克力", blue: "蓝灰", silver: "银灰", lilac: "丁香灰", charcoal: "炭黑", black: "墨黑"
  };
  const eyes = {original: "原画", amber: "琥珀", gold: "金黄", copper: "铜棕", hazel: "榛褐", green: "绿", aqua: "青绿", blue: "蓝"};
  const skins = {rose: "玫瑰粉", sand: "暖沙", pearl: "珍珠粉", slate: "石板灰", cocoa: "可可棕"};
  const ears = {upright: "立耳", slight: "微折耳", folded: "折耳"};
  const brows = {original: "原画", none: "无眉纹", fine: "细眉", bold: "浓眉", arched: "弯月眉", points: "双眉点"};
  const nosePigments = {auto: "随毛色或肤色", rose: "粉鼻", spotted: "粉底黑斑", black: "黑鼻", dark: "黑鼻"};
  const earPigments = {auto: "随毛色或肤色", rose: "蔷薇粉", smoke: "烟灰", dark: "墨黑"};
  const labels = {
    traits: {
      affection: "亲人", irritability: "易躁", curiosity: "好奇", activity: "活跃", nocturnality: "夜行",
      huntingDrive: "猎性", reaction: "反应", nightHuntingReaction: "夜捕反应", vigilance: "警觉"
    },
    physique: {
      satietyDrainFactor: "饱腹消耗倍率", maximumEnergyFactor: "最大体力倍率", restRecoveryFactor: "休息恢复倍率",
      badWaterIllnessFactor: "坏水生病倍率", juvenileDays: "幼年（天）", adultDays: "成年（天）", seniorDays: "老年（天）"
    },
    modifiers: {
      suppressWhiteIrritabilityBonus: "抑色易躁加成", noiseAvoidanceBonus: "嘈杂回避加成",
      unfamiliarNightSoundAvoidanceBonus: "夜间陌生动静回避加成", climbingAndWheelEnergyFactor: "攀爬跑轮消耗倍率",
      scrapHeapSearchBonus: "废料堆搜索加成"
    }
  };

  function element(tag, text, className) {
    const item = document.createElement(tag);
    if (text !== undefined) item.textContent = text;
    if (className) item.className = className;
    return item;
  }

  function readPercent(n) {
    return (Number(n) * 100).toLocaleString("zh-CN", {maximumFractionDigits: 2}) + "%";
  }


  function traitBlock(cat) {
    const p = cat.phenotype;
    const look = p.appearance;
    const box = element("div", undefined, "expressed");
    const primary = element("p");
    const traits = [];
    if (p.whiteCoat) traits.push("纯白");
    else if (p.fur === "hairless") traits.push(skins[look.skinColor]);
    else {
      const marking = markings[p.marking];
      const replacesCoat = p.marking === "point" || p.marking === "spotted";
      traits.push(colors[look.baseColor], replacesCoat ? marking : coats[look.coatPattern]);
      if (!replacesCoat && marking !== "普通") traits.push(marking);
    }
    primary.append(element("strong", [fur[p.fur], ...traits, ears[look.earShape]].filter(Boolean).join(" · ")));
    const iris = look.eyeMode === "odd"
      ? `异瞳 ${eyes[look.eyeLeft]} / ${eyes[look.eyeRight]}` : `${eyes[look.eyeLeft]}眼`;
    box.append(primary, element("p", `${iris} · 易躁 ${p.irritability} · 缓冲 ${p.bufferCount} 位`));
    const notable = [
      p.paternalNoseSpot && "父系鼻斑", p.slowRecovery && "恢复慢", p.badWaterSensitive && "坏水敏感"
    ].filter(Boolean);
    if (notable.length) box.append(element("p", notable.join(" · "), "extra-traits"));
    return box;
  }

  function chromosomeTable(cat) {
    const table = element("table", undefined, "chromosomes");
    const header = table.createTHead().insertRow();
    for (const title of ["组", "母源序列", "父源序列"]) {
      const th = element("th", title);
      th.scope = "col";
      header.append(th);
    }
    function sequence(title, maternal, paternal) {
      const row = body.insertRow();
      const group = element("th", title);
      group.scope = "row";
      row.append(group);
      for (const bits of [maternal, paternal]) {
        const cell = row.insertCell();
        const code = element("code");
        for (const bit of bits) code.append(element("span", bit, bit === "1" ? "bit-on" : "bit-off"));
        cell.append(code);
      }
    }
    const body = table.createTBody();
    groups.forEach((group, index) => sequence(group, cat.genome.maternal[index], cat.genome.paternal[index]));
    sequence("X / Y", "X " + cat.genome.maternalX, (cat.sex === "XY" ? "Y " : "X ") + cat.genome.paternalSex);
    return table;
  }

  function detailedTraits(phenotype) {
    const pane = element("details", undefined, "traits-details");
    pane.open = detail.querySelector("details")?.open ?? false;
    pane.append(element("summary", "全部表达参数"));
    const values = element("dl");
    function append(name, value) {
      const cell = element("div");
      cell.append(element("dt", name), element("dd", value));
      values.append(cell);
    }
    const look = phenotype.appearance;
    append("丰盈度", readPercent(look.fullness));
    append("脸颊宽度", readPercent(look.cheeks));
    append("口鼻长度", readPercent(look.muzzle));
    append("眼形", readPercent(look.eyes));
    append("耳廓高度", readPercent(look.ears));
    append("眉纹", brows[look.browType]);
    append("鼻色", nosePigments[look.nosePigment]);
    append("内耳色", earPigments[look.earPigment]);
    append("纹路色", colors[look.patchColor]);
    append("纹路密度", readPercent(look.bands));
    append("白斑范围", readPercent(look.white));
    for (const [section, names] of Object.entries(labels)) {
      for (const [key, name] of Object.entries(names)) {
        const number = phenotype[section]?.[key];
        if (number !== undefined) append(name, number.toLocaleString("zh-CN", {maximumFractionDigits: 2}));
      }
    }
    pane.append(values);
    return pane;
  }

  function inspect(cat, mode) {
    if (!cat) return;
    if (inspectedId === cat.id && inspectionMode === mode) return;
    inspectedId = cat.id;
    inspectionMode = mode;
    get("inspector-mode").textContent = mode;
    const heading = element("h3", `${cat.id}  ${cat.sex}`, "inspector-name");
    const relatives = element("p", undefined, "parent-caption");
    for (const [label, id] of [["母猫", cat.motherId], ["公猫", cat.fatherId]]) {
      if (!cats.has(id)) continue;
      const button = element("button", `${label} ${id}`);
      button.type = "button";
      button.addEventListener("click", () => choose(cats.get(id), true));
      relatives.append(button);
    }
    if (!relatives.hasChildNodes()) relatives.textContent = "独立血系源头";
    detail.replaceChildren(heading, relatives, traitBlock(cat), chromosomeTable(cat), detailedTraits(cat.phenotype));
  }


  const project = generation => start + generation * step;

  function attachCard(cat, generation, role) {
    const button = element("button", undefined, "cat-node");
    button.type = "button";
    button.dataset.cat = cat.id;
    button.dataset.role = role;
    button.dataset.generation = generation;
    if (cat.phenotype.whiteCoat) button.dataset.white = "true";
    const sex = cat.sex === "XY" ? "公" : "母";
    const roleLabel = role === "mate" ? "外配" : "主藤";
    button.setAttribute("aria-label", '第 ' + generation + ' 代，' + roleLabel + '，' + cat.id + '，' + sex + '猫');
    button.setAttribute("aria-pressed", "false");
    const box = element("span", undefined, "cat-preview");
    const load = element("span", "绘制中", "loading");
    box.append(load);
    const description = element("span", undefined, "node-info");
    description.append(element("strong", roleLabel + ' · ' + sex), document.createTextNode(" · " + fur[cat.phenotype.fur]));
    button.append(box, element("span", cat.id, "cat-id"), description);

    const left = project(generation);
    const top = role === "mate" ? mateTop : vineTop;
    button.style.left = left + "px";
    button.style.top = top + "px";
    const position = {cat, button, box, load, left, top, generation, role, loaded: false};
    records.push(position);
    positions.set(cat.id, position);
    button.addEventListener("pointerenter", event => {
      // A narrow viewport uses a full-width inspector. Wait for a click so
      // a hover cannot cover the node before the user presses it.
      if (event.pointerType === "touch" || viewport.clientWidth < 840 || dragging?.moving || zoom < .34) return;
      clearTimeout(hoverTimer);
      inspectPosition(position, "悬停预览");
    });
    button.addEventListener("pointerleave", restoreInspectionSoon);
    button.addEventListener("focus", () => {
      if (!button.matches(":focus-visible")) return;
      if (!inView(position, 0)) panTo(generation);
      inspectPosition(position, "键盘焦点");
    });
    button.addEventListener("blur", restoreInspectionSoon);
    button.addEventListener("click", () => {
      if (performance.now() >= suppressClickUntil) choose(cat, zoom < .34);
    });
    layer.append(button);
    return position;
  }

  function drawEdge(d, className) {
    const path = document.createElementNS("http://www.w3.org/2000/svg", "path");
    path.setAttribute("d", d);
    path.setAttribute("class", className);
    edges.append(path);
  }

  function buildGraph() {
    world.style.width = worldWidth + "px";
    world.style.height = worldHeight + "px";
    edges.setAttribute("width", worldWidth);
    edges.setAttribute("height", worldHeight);
    const middle = vineTop + cardHeight / 2;
    mainCats.forEach((cat, generation) => {
      const {left} = attachCard(cat, generation, "vine");
      const tag = element("span", '第 ' + generation + ' 代', "generation-tag");
      if (generation % 25 === 0 || generation === finalGeneration) tag.classList.add("milestone");
      tag.style.left = left + "px";
      tag.style.top = vineTop - 30 + "px";
      layer.append(tag);
      const tick = element("i");
      if (cat.phenotype.whiteCoat) tick.className = "is-white";
      get("timeline-track").append(tick);

      const mate = partners[generation];
      if (!mate) return;
      const next = project(generation + 1);
      drawEdge("M " + (left + cardWidth) + " " + middle + " H " + next, "vine-edge");
      attachCard(mate, generation, "mate");
      const above = left + cardWidth / 2;
      const startY = mateTop + cardHeight;
      const corridor = next - 23;
      const junction = middle - 35;
      drawEdge("M " + above + " " + startY + " C " + above + " " + (startY + 35) + ", "
        + corridor + " " + (startY + 12) + ", " + corridor + " " + junction
        + " Q " + corridor + " " + middle + ", " + next + " " + middle, "mate-edge");
    });
    const ticks = [...new Set([0, .25, .5, .75, 1].map(f => Math.round(f * finalGeneration)))];
    get("timeline-ticks").append(...ticks.map(value => element("span", String(value))));
  }

  function restoreInspectionSoon() {
    clearTimeout(hoverTimer);
    hoverTimer = setTimeout(() => {
      const position = positions.get(selectedCat?.id);
      if (position) inspectPosition(position, "已选个体");
      else panel.hidden = true;
    }, 140);
  }

  function inspectPosition(position, mode) {
    clearTimeout(hoverTimer);
    panel.dataset.side = (x + zoom * (position.left + cardWidth / 2)) >= viewport.clientWidth * .55 ? "left" : "right";
    panel.hidden = false;
    inspect(position.cat, mode);
  }

  function closeDetail() {
    clearTimeout(hoverTimer);
    selectedCat = null;
    panel.hidden = true;
    markSelected();
  }

  function markSelected() {
    for (const {cat, button} of records) button.setAttribute("aria-pressed", String(cat.id === selectedCat?.id));
  }

  function choose(cat, center = false) {
    const position = positions.get(cat.id);
    if (!position) return;
    selectedCat = cat;
    if (center || !inView(position, 0)) {
      if (zoom < .34) zoom = .9;
      panTo(position.generation);
    } else syncControls(position.generation);
    markSelected();
    inspectPosition(position, "已选个体");
  }

  function minZoom() {
    return Math.min(1, viewport.clientWidth / (worldWidth + 45), viewport.clientHeight / (worldHeight + 32));
  }

  function clampOffset(offset, size, available) {
    return size <= available ? (available - size) / 2 : Math.max(available - size, Math.min(0, offset));
  }

  function syncControls(generation) {
    current = Math.max(0, Math.min(finalGeneration, generation));
    if (!editingGeneration) number.value = current;
    timeline.value = current;
    const pct = minZoom();
    get("zoom-value").value = Math.round(100 * zoom) + "%";
    get("zoom-out").disabled = zoom <= pct + .001;
    get("zoom-in").disabled = zoom >= 1.8;
    get("fit").setAttribute("aria-pressed", String(fitted));
    get("previous").disabled = current === 0;
    get("next").disabled = current === finalGeneration;
  }

  function redraw(generation) {
    x = clampOffset(x, worldWidth * zoom, viewport.clientWidth);
    y = clampOffset(y, worldHeight * zoom, viewport.clientHeight);
    world.style.transform = "translate(" + x + "px," + y + "px) scale(" + zoom + ")";
    world.style.setProperty("--inverse-scale", 1 / zoom);
    world.classList.toggle("overview", zoom < .34);
    const centerGeneration = Math.round(((viewport.clientWidth / 2 - x) / zoom - start - cardWidth / 2) / step);
    syncControls(generation ?? centerGeneration);
    clearTimeout(paintTimer);
    paintTimer = setTimeout(renderNextPreview, 70);
  }

  function panTo(generation) {
    fitted = false;
    x = viewport.clientWidth / 2 - (project(generation) + cardWidth / 2) * zoom;
    redraw(generation);
  }

  function navigate(value) {
    if (!Number.isFinite(value)) return;
    const generation = Math.max(0, Math.min(finalGeneration, Math.round(value)));
    if (zoom < .38) zoom = 1;
    panTo(generation);
  }

  function fit() {
    fitted = true;
    zoom = minZoom();
    redraw(current);
  }

  function zoomAt(factor, px = viewport.clientWidth / 2, py = viewport.clientHeight / 2) {
    const nextZoom = Math.max(minZoom(), Math.min(1.8, zoom * factor));
    const ratio = nextZoom / zoom;
    x = px - (px - x) * ratio;
    y = py - (py - y) * ratio;
    zoom = nextZoom;
    fitted = false;
    redraw();
  }

  function inView(position, margin) {
    const left = x + position.left * zoom;
    const top = y + position.top * zoom;
    return left < viewport.clientWidth + margin && left + cardWidth * zoom > -margin
      && top < viewport.clientHeight + margin && top + cardHeight * zoom > -margin;
  }

  async function renderNextPreview() {
    if (previewBusy || previewFailed || dragging?.moving || zoom < .34) return;
    const priority = position =>
      (inView(position, 0) ? 0 : 10000) + (position.role === "vine" ? 0 : 1000) + Math.abs(position.generation - current);
    const job = records.filter(position => !position.loaded && inView(position, 120)).sort((a, b) => priority(a) - priority(b))[0];
    if (!job) return;
    previewBusy = true;
    try {
      previewJob ??= window.CatPedigreePreviews.create();
      const renderer = await previewJob;
      const source = await renderer.image(job.cat);
      const image = element("img");
      image.alt = "编号 " + job.cat.id + " 的成猫正面格内图";
      image.src = source;
      job.box.prepend(image);
      job.box.classList.add("is-ready");
      job.loaded = true;
    } catch {
      previewFailed = true;
      get("preview-status").textContent = "格内预览未加载，仍可查看基因与亲缘。";
      get("preview-status").hidden = false;
    } finally {
      previewBusy = false;
      renderNextPreview();
    }
  }

  viewport.addEventListener("wheel", event => {
    event.preventDefault();
    if (event.shiftKey) {
      x -= event.deltaX || event.deltaY;
      fitted = false;
      redraw();
      return;
    }
    const rect = viewport.getBoundingClientRect();
    const delta = event.deltaY * (event.deltaMode === 1 ? 16 : 1);
    zoomAt(Math.exp(-Math.max(-200, Math.min(200, delta)) * .002), event.clientX - rect.left, event.clientY - rect.top);
  }, {passive: false});

  viewport.addEventListener("pointerdown", event => {
    if (event.button !== 0 || dragging) return;
    dragging = {id: event.pointerId, startX: event.clientX, startY: event.clientY, x, y, moving: false};
  });

  viewport.addEventListener("pointermove", event => {
    if (dragging?.id !== event.pointerId) return;
    const dx = event.clientX - dragging.startX;
    const dy = event.clientY - dragging.startY;
    if (!dragging.moving && Math.hypot(dx, dy) < 5) return;
    if (!dragging.moving) {
      dragging.moving = true;
      viewport.setPointerCapture(event.pointerId);
      viewport.classList.add("is-dragging");
      if (!selectedCat) panel.hidden = true;
    }
    x = dragging.x + dx;
    y = dragging.y + dy;
    fitted = false;
    redraw();
  });

  function finishDrag(event) {
    if (dragging?.id !== event.pointerId) return;
    const moved = dragging.moving;
    dragging = null;
    viewport.classList.remove("is-dragging");
    if (moved) {
      suppressClickUntil = performance.now() + 200;
      redraw();
    }
  }

  viewport.addEventListener("pointerup", finishDrag);
  viewport.addEventListener("pointercancel", finishDrag);
  viewport.addEventListener("lostpointercapture", finishDrag);
  viewport.addEventListener("pointerleave", event => {
    if (!dragging?.moving) finishDrag(event);
  });
  viewport.addEventListener("keydown", event => {
    const direction = {"ArrowLeft": 1, "ArrowRight": -1, "ArrowUp": 1, "ArrowDown": -1}[event.key];
    if (direction) {
      event.preventDefault();
      fitted = false;
      if (event.key === "ArrowLeft" || event.key === "ArrowRight") x += direction * 95;
      else y += direction * 80;
      redraw();
    } else if (event.key === "Home" || event.key === "End") {
      event.preventDefault();
      navigate(event.key === "Home" ? 0 : finalGeneration);
    } else if (event.key === "+" || event.key === "=" || event.key === "-") {
      event.preventDefault();
      zoomAt(event.key === "-" ? 1 / 1.25 : 1.25);
    }
  });

  number.max = timeline.max = finalGeneration;
  function navigateFromNumber() {
    editingGeneration = false;
    if (number.value.trim()) {
      if (Number(number.value) !== current || number.value !== String(current)) navigate(Number(number.value));
    }
    else number.value = current;
  }
  number.addEventListener("input", () => { editingGeneration = true; });
  number.addEventListener("change", navigateFromNumber);
  number.addEventListener("blur", navigateFromNumber);
  number.addEventListener("keydown", event => {
    if (event.key === "Enter") {
      event.preventDefault();
      navigateFromNumber();
    }
  });
  timeline.addEventListener("input", () => navigate(Number(timeline.value)));
  get("previous").addEventListener("click", () => navigate(current - 1));
  get("next").addEventListener("click", () => navigate(current + 1));
  get("zoom-out").addEventListener("click", () => zoomAt(1 / 1.25));
  get("zoom-in").addEventListener("click", () => zoomAt(1.25));
  get("fit").addEventListener("click", fit);
  get("close-detail").addEventListener("click", closeDetail);
  panel.addEventListener("pointerenter", () => clearTimeout(hoverTimer));
  panel.addEventListener("pointerleave", restoreInspectionSoon);
  document.addEventListener("keydown", event => {
    if (event.key !== "Escape") return;
    if (!panel.hidden) closeDetail();
    else if (graph.classList.contains("expanded")) expand(false);
  });

  function fullScreenButton(active) {
    const button = get("fullscreen");
    button.setAttribute("aria-pressed", String(active));
    button.textContent = active ? "退出全屏" : "全屏";
  }

  function expand(active) {
    graph.classList.toggle("expanded", active);
    document.body.classList.toggle("canvas-expanded", active);
    fullScreenButton(active);
  }

  get("fullscreen").addEventListener("click", async () => {
    if (graph.classList.contains("expanded")) {
      expand(false);
      return;
    }
    try {
      if (document.fullscreenElement === graph) await document.exitFullscreen();
      else if (graph.requestFullscreen) await graph.requestFullscreen();
      else throw new Error("Fullscreen unavailable");
    } catch {
      expand(true);
    }
  });
  document.addEventListener("fullscreenchange", () => fullScreenButton(document.fullscreenElement === graph));
  new ResizeObserver(() => {
    if (fitted) fit();
    else redraw(current);
  }).observe(viewport);

  get("run-length").textContent = finalGeneration;
  get("run-preset").textContent = mainCats.length + " 只主藤猫 · " + (cats.size - mainCats.length) + " 只外配猫 · 随机种子 " + study.seed;
  get("white-count").textContent = mainCats.filter(cat => cat.phenotype.whiteCoat).length;
  get("cream-count").textContent = mainCats.filter(cat =>
    cat.phenotype.fur !== "hairless" && !cat.phenotype.whiteCoat && cat.phenotype.appearance.baseColor === "cream"
  ).length;
  const {whiteAlleleChance, dilutionAlleleChance, crossoverChance, mutationChance} = study.settings;
  get("run-notes").textContent = "外配新血系 · 抑色位单份 " + readPercent(whiteAlleleChance) + " · 稀释位单份 "
    + readPercent(dilutionAlleleChance) + " · 重组 " + readPercent(crossoverChance) + " · 突变 " + readPercent(mutationChance);
  buildGraph();
  graph.hidden = false;
  const query = new URLSearchParams(location.search);
  const requestedCat = cats.get(query.get("cat"));
  if (requestedCat) choose(requestedCat, true);
  else navigate(Number(query.get("generation") ?? 0));
  window.addEventListener("pagehide", event => {
    if (!event.persisted) {
      clearTimeout(paintTimer);
      previewJob?.then(renderer => renderer.dispose(), () => {});
    }
  });
})();
