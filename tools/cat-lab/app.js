(async () => {
  "use strict";
  const defaults = window.CatModel.defaults;
  const {modes, markings, palette} = window.CatCoat;
  const eyes = Object.entries(window.CatIris.palette).map(([value,label]) => ({
    value, label, hex:window.CatIris.swatches?.[value]
  }));
  const furLengths = [
    {value:"hairless", label:"无毛"}, {value:"short", label:"短毛"}, {value:"long", label:"长毛"}
  ];
  const eyeModes = [{value:"matched", label:"普通瞳色"}, {value:"odd", label:"异瞳"}];
  const earShapes = [{value:"upright",label:"立耳"},{value:"slight",label:"微折耳"},{value:"folded",label:"折耳"}];
  const noseSeedRange = ["noseSeed","鼻斑分布","位置 A","位置 B"];
  const traits = {
    shape: [
      ["fullness", "体态丰盈度", "清瘦", "圆胖"],
      ["cheeks", "脸颊宽度", "窄脸", "圆腮"],
      ["muzzle", "口鼻长度", "短吻", "长吻"],
      ["ears", "耳廓高度", "短耳", "高耳"],
      ["eyes", "眼形", "细窄", "圆大"]
    ],
    coat: [
      ["hue", "底毛色调", "赤棕", "炭灰"],
      ["bands", "虎斑密度", "稀疏", "细密"],
      ["white", "白斑覆盖", "少", "多"],
      ["patternSeed", "花纹分布", "位置 A", "位置 B"]
    ]
  };
  const controls = document.querySelector("#sliders");
  const message = document.querySelector("#render-message");
  const canvas = document.querySelector("#model-canvas");
  const query = new URLSearchParams(location.search);
  const choices = {furLength:furLengths, skinColor:window.CatModel.skins, eyeMode:eyeModes,
    eyeLeft:eyes, eyeRight:eyes, coatPattern:modes, marking:markings, baseColor:palette, patchColor:palette,
    earShape:earShapes, browType:window.CatBrows.types, earPigment:window.CatPigment.ears, nosePigment:window.CatPigment.noses};
  const ranges = [...traits.shape,...traits.coat,noseSeedRange].map(([key])=>key);
  function readExpression(params=query) {
    const expression = {...defaults,age:params.get("age")==="kitten" ? "kitten" : "adult"};
    for (const [key,options] of Object.entries(choices)) {
      const selected = options.find(option=>String(option.value)===params.get(key));
      if (selected) expression[key]=selected.value;
    }
    for (const key of ranges) {
      const raw=params.get(key);
      const value=Number(raw);
      if (raw && Number.isFinite(value) && value>=0 && value<=1) expression[key]=value;
    }
    if (expression.eyeMode==="odd" && expression.eyeRight===expression.eyeLeft) {
      expression.eyeRight=expression.eyeLeft==="blue" ? "amber" : "blue";
    }
    return expression;
  }
  let state = readExpression();
  let comparing = query.get("compare") === "1";
  const otherAge = () => state.age === "adult" ? "kitten" : "adult";
  let reference = comparing && query.has("companion")
    ? {...readExpression(new URLSearchParams(query.get("companion"))),age:otherAge()}
    : {...state, age:otherAge()};
  let lastPair = {};
  const panels = ["features","coat","shape"];
  let group = panels.includes(query.get("panel")) ? query.get("panel") : "features";
  let viewer;
  let frame = 0;
  const edition=document.querySelector("#model-edition");
  edition.value=edition.dataset.current;
  window.addEventListener("pageshow",()=>{edition.value=edition.dataset.current;});
  function defaultView() {
    return edition.dataset.current==="front" && !comparing ? "front" : "fixed";
  }
  let restoreCamera = true;
  function expressionParams(genes) {
    const params=new URLSearchParams();
    for (const [key,value] of Object.entries(genes)) {
      if (value!==defaults[key]) params.set(key,String(value));
    }
    return params;
  }
  edition.addEventListener("change",()=>{
    const target=new URL(edition.value==="front" ? "index.html" : "rotation.html",location.href);
    const params=expressionParams(state);
    params.set("panel",group);
    if(comparing) {
      params.set("compare","1");
      params.set("companion",expressionParams(reference).toString());
    }
    if(viewer) {
      const {view,yaw,materialMode}=viewer.getCameraState();
      params.set("view",view);
      if(view==="free")params.set("yaw",String(yaw));
      if(materialMode==="clay")params.set("material","clay");
    }
    target.search=params.toString();
    location.assign(target.href);
  });

  function pressed(attribute, value) {
    document.querySelectorAll("[data-"+attribute+"]").forEach(button => {
      button.setAttribute("aria-pressed", String(button.dataset[attribute] === value));
    });
  }
  function rememberExpression() {
    if (comparing || location.protocol==="file:") return;
    const url=new URL(location.href);
    for (const [key,value] of Object.entries(state)) {
      if (value===defaults[key]) url.searchParams.delete(key);
      else url.searchParams.set(key,String(value));
    }
    if (url.href!==location.href) history.replaceState(null,"",url);
  }
  function render() {
    if (frame || !viewer) return;
    frame = requestAnimationFrame(() => {
      frame = 0;
      try {
        viewer.update(state, comparing ? reference : null);
        if(restoreCamera) {
          restoreCamera=false;
          const requestedView=query.get("view");
          const view=["fixed","front","side","free"].includes(requestedView) ? requestedView : defaultView();
          if(view==="free" && query.has("yaw") && Number.isFinite(Number(query.get("yaw")))) {
            viewer.rotate(Number(query.get("yaw"))-viewer.getCameraState().yaw);
          } else viewer.setView(view==="free" ? defaultView() : view);
          if(query.get("material")==="clay") setMaterial("clay");
        }
        message.hidden = true;
      } catch (error) {
        console.error(error);
        message.hidden = false;
        message.textContent = "模型绘制失败，请刷新页面后重试。";
      }
    });
  }
  function syncStage() {
    pressed("age",state.age);
    document.querySelector("#compare").setAttribute("aria-pressed", String(comparing));
    const adult = state.age === "adult";
    document.querySelector('[data-view="fixed"]').textContent = comparing ? "合照" : "斜前";
    document.querySelector("#controls-title").lastChild.textContent =
      comparing ? (adult ? "成猫 · 基因表达" : "幼猫 · 基因表达") : "基因表达";
    document.querySelector("#occupation").innerHTML = adult ? "坐姿 <b>2 × 4</b> 格" : "趴姿 <b>2 × 1</b> 格";
    document.querySelector("#icon-canvas").setAttribute("aria-label", adult ? "正面坐姿成猫，2 × 4 格" : "正面趴姿幼猫，2 × 1 格");
  }
  function addRange([key,title,min,max]) {
    const numbered = key==="noseSeed" || (key==="patternSeed" && (state.coatPattern!==0 || state.marking!==0));
    const limit = numbered ? 65535 : 100;
    const row = document.createElement("div");
    row.className = "trait";
    const id = "trait-"+key;
    row.innerHTML = '<div class="trait-head"><label for="'+id+'">'+title+'</label><output for="'+id+'"></output></div>'+
      '<input type="range" id="'+id+'" min="0" max="100" step="1" aria-describedby="'+id+'-ends">'+
      '<div id="'+id+'-ends" class="ends"><span>'+min+'</span><span>'+max+'</span></div>';
    const input = row.querySelector("input");
    const output = row.querySelector("output");
    input.max = limit;
    input.value = Math.round(state[key]*limit);
    if (numbered) {
      row.querySelector("label").textContent=key==="noseSeed" ? "鼻斑编号" : "花纹编号";
      const ends=row.querySelector(".ends").children;
      ends[0].textContent="0"; ends[1].textContent=String(limit);
      const shuffle=document.createElement("button");
      shuffle.type="button";
      shuffle.className="reroll";
      shuffle.textContent="换个分布";
      shuffle.addEventListener("click",()=>{
        input.value=(input.valueAsNumber+1+Math.floor(Math.random()*limit))%(limit+1);
        input.dispatchEvent(new Event("input"));
        rememberExpression();
      });
      output.before(shuffle);
    }
    function syncValue() {
      output.value = input.value;
      input.style.setProperty("--progress", (100*input.valueAsNumber/limit)+"%");
    }
    input.addEventListener("input", () => {
      state[key] = input.valueAsNumber / limit;
      syncValue();
      render();
    });
    input.addEventListener("change",rememberExpression);
    syncValue();
    controls.append(row);
  }
  function randomOption(options, except) {
    const pool = except === undefined ? options : options.filter(option=>option.value!==except);
    return pool[Math.floor(Math.random()*pool.length)].value;
  }
  function changeChoice(key, value) {
    const old = state[key];
    state[key] = value;
    if (state.eyeMode === "odd" && state.eyeRight === state.eyeLeft) {
      // Keep both eyes visibly distinct, including when one takes the other's color.
      if (key === "eyeRight") state.eyeLeft = old;
      else if (key === "eyeLeft") state.eyeRight = old;
      else state.eyeRight = state.eyeLeft === "blue" ? "amber" : "blue";
    }
    drawControls();
    render();
  }
  function addChoice(key, title, options, colored = false) {
    const row = document.createElement("div");
    row.className = "trait choice-row";
    const label = document.createElement("label");
    label.htmlFor = "trait-"+key;
    label.textContent = title;
    const field = document.createElement("div");
    field.className = "choice-field";
    const select = document.createElement("select");
    select.id = label.htmlFor;
    for (const {value,label:name} of options) select.add(new Option(name,String(value)));
    select.value = String(state[key]);
    select.addEventListener("change", () => {
      const option = options.find(item=>String(item.value)===select.value);
      changeChoice(key, option.value);
    });
    if (colored) {
      const chip = document.createElement("span");
      chip.className = "color-chip";
      const hex = options.find(item=>item.value===state[key])?.hex;
      if (hex) chip.style.background = hex;
      chip.setAttribute("aria-hidden","true");
      field.append(chip);
    }
    field.append(select);
    row.append(label,field);
    controls.append(row);
  }
  function drawControls() {
    rememberExpression();
    const focused = controls.contains(document.activeElement) ? document.activeElement.id : null;
    pressed("group",group);
    controls.replaceChildren();
    if (group === "shape") {
      addChoice("earShape","耳形",earShapes);
      addChoice("browType","眉纹",window.CatBrows.types);
      traits.shape.forEach(addRange);
    } else if (group === "features") {
      addChoice("furLength","毛长",furLengths);
      if (state.furLength==="hairless") addChoice("skinColor","皮肤色",window.CatModel.skins,true);
      addChoice("eyeMode","双眼配色",eyeModes);
      const odd = state.eyeMode==="odd";
      addChoice("eyeLeft",odd ? "猫的左眼" : "瞳色",eyes,true);
      if (odd) addChoice("eyeRight","猫的右眼",eyes,true);
      addChoice("earPigment","内耳色",window.CatPigment.ears);
      addChoice("nosePigment","鼻头色",window.CatPigment.noses);
      if (state.nosePigment==="spotted") addRange(noseSeedRange);
    } else if (state.furLength==="hairless") {
      addChoice("skinColor","皮肤色",window.CatModel.skins,true);
      const note = document.createElement("p");
      note.className = "coat-note";
      note.textContent = "无毛猫显露皮肤色。切换到短毛或长毛，即可查看这只猫的被毛花纹。";
      controls.append(note);
      addChoice("furLength","毛长",furLengths);
    } else {
      const mode = state.coatPattern;
      const mark = state.marking;
      const pointed = mark===4;
      const painted = mode===0 && mark<4;
      addChoice("coatPattern","毛纹类型",modes);
      addChoice("marking","特殊花色",markings);
      if (!painted) addChoice("baseColor","底色",palette,true);
      if (pointed || mark===7 || mode>=2) {
        const accents = {2:"毛尖色",3:"条纹色",4:"色块颜色",5:"拼色"};
        addChoice("patchColor",pointed ? "重点色" : mark===7 ? "斑点色" : accents[mode],palette,true);
      }
      const [hue,bands,white,seed] = traits.coat;
      if (!pointed) {
        if (painted) addRange(hue);
        if (painted || mode===3 || mark===7) addRange(bands);
        if (painted || mode===4 || (mark>0 && mark<7)) addRange(white);
        if (mode!==1 || mark!==0) addRange(seed);
      }
    }
    if (group==="coat") {
      const sample=document.createElement("button");
      sample.type="button";
      sample.className="sample-black";
      sample.textContent="试试全黑猫";
      sample.addEventListener("click",()=>{
        Object.assign(state,{coatPattern:1,baseColor:"black",marking:0,earPigment:"auto",nosePigment:"auto"});
        if (state.furLength==="hairless") state.furLength="short";
        drawControls();
        render();
      });
      controls.append(sample);
    }
    if (focused) document.getElementById(focused)?.focus({preventScroll:true});
  }
  document.querySelectorAll("[data-age]").forEach(button => button.addEventListener("click", () => {
    if (button.dataset.age === state.age) return;
    if (comparing) [state,reference] = [reference,state];
    else state = {...state, age:button.dataset.age};
    syncStage();
    drawControls();
    render();
  }));
  document.querySelector("#compare").addEventListener("click", () => {
    comparing = !comparing;
    if (comparing) reference = lastPair[otherAge()] || {...state, age:otherAge()};
    else lastPair = {[state.age]:state, [reference.age]:reference};
    const url = new URL(location.href);
    if (comparing) url.searchParams.set("compare","1");
    else url.searchParams.delete("compare");
    if (location.protocol !== "file:") history.replaceState(null,"",url);
    rememberExpression();
    syncStage();
    render();
  });
  document.querySelectorAll("[data-group]").forEach(button => button.addEventListener("click", () => {
    group = button.dataset.group;
    if (location.protocol !== "file:") {
      const url = new URL(location.href);
      url.searchParams.set("panel",group);
      history.replaceState(null,"",url);
    }
    drawControls();
  }));
  document.querySelectorAll("[data-view]").forEach(button => button.addEventListener("click", () => {
    viewer?.setView(button.dataset.view);
  }));
  function setMaterial(mode) {
    viewer?.setMaterialMode(mode);
    pressed("material", mode);
  }
  document.querySelectorAll("[data-material]").forEach(button => button.addEventListener("click", () => {
    setMaterial(button.dataset.material);
  }));
  canvas.addEventListener("keydown", event => {
    if (event.key !== "ArrowLeft" && event.key !== "ArrowRight") return;
    event.preventDefault();
    viewer?.rotate(event.key === "ArrowLeft" ? -.15 : .15);
  });
  document.querySelector("#reset").addEventListener("click", () => {
    state = {...defaults, age:comparing ? state.age : defaults.age};
    viewer?.setView(defaultView());
    setMaterial("coat");
    syncStage();
    drawControls();
    render();
  });
  document.querySelector("#random").addEventListener("click", () => {
    for (const [key] of [...traits.shape,...traits.coat]) state[key] = Math.floor(Math.random()*101)/100;
    state.patternSeed=Math.floor(Math.random()*65536)/65535;
    state.furLength = randomOption(furLengths);
    state.skinColor = randomOption(window.CatModel.skins);
    state.eyeMode = randomOption(eyeModes);
    state.eyeLeft = randomOption(eyes);
    state.eyeRight = randomOption(eyes,state.eyeLeft);
    state.coatPattern = randomOption(modes);
    state.marking = randomOption(markings);
    state.baseColor = randomOption(palette);
    state.patchColor = randomOption(palette,state.baseColor);
    state.browType = randomOption(window.CatBrows.types);
    state.earShape = Math.random()<.66 ? "upright" : randomOption(earShapes,"upright");
    state.earPigment = randomOption(window.CatPigment.ears);
    state.nosePigment = randomOption(window.CatPigment.noses);
    state.noseSeed = Math.floor(Math.random()*65536)/65535;
    drawControls();
    render();
  });
  syncStage();
  drawControls();
  try {
    await window.CatModel.prepare(window.THREE);
    viewer = window.createCatViewer({
      canvas, iconCanvas: document.querySelector("#icon-canvas"), onViewChange: view => pressed("view", view)
    });
    render();
  } catch (error) {
    console.error(error);
    message.textContent = "无法启动三维预览。请使用已开启图形加速的浏览器打开此页。";
  }
  window.addEventListener("pagehide", event => {
    if (!event.persisted) {
      viewer?.destroy();
      window.CatModel.dispose();
    }
  });
})();
