/* Each carving has two registered paint coordinates per vertex. These
   coordinates are bound to the rest pose and follow changes in body shape. */
(() => {
  "use strict";
  const atlases = {};
  const kittenHeadTurn = .42;
  const defaults = Object.freeze({
    age:"adult", fullness:.50, cheeks:.50, muzzle:.50, ears:.50, eyes:.50,
    bands:.25, white:.55, hue:.25, patternSeed:.36,
    furLength:"short", skinColor:"rose", eyeMode:"matched", eyeLeft:"original", eyeRight:"blue",
    coatPattern:0, marking:0, baseColor:"ginger", patchColor:"charcoal",
    earShape:"upright", browType:"original", earPigment:"auto", nosePigment:"auto", noseSeed:.42
  });
  const skins = [
    {value:"rose",label:"玫瑰粉",hex:"#D49F99"}, {value:"sand",label:"暖沙",hex:"#C69C83"},
    {value:"pearl",label:"珍珠粉",hex:"#ECD1C8"}, {value:"slate",label:"石板灰",hex:"#938A99"},
    {value:"cocoa",label:"可可棕",hex:"#936E64"}
  ];
  function pigment(T, palette, value) {
    const color = (palette.find(option=>option.value===value) || palette[0]).hex;
    const rgb = parseInt(color.slice(1),16);
    return new T.Vector3(rgb>>16,(rgb>>8)&255,rgb&255).divideScalar(255);
  }
  async function prepare(T) {
    const loader = new T.TextureLoader();
    await Promise.all(Object.entries(window.CatAtlases).map(async ([stage, images]) => {
      const [paint, fields, finish, leather] = await Promise.all(
        [images.paint,images.fields,images.finish,images.leather].map(url=>loader.loadAsync(url)));
      paint.anisotropy = 4;
      finish.anisotropy = 4;
      leather.anisotropy = 4;
      atlases[stage] = {paint, fields, finish, leather};
    }));
  }

  const varying = `
    varying vec2 paintedFront;
    varying vec2 paintedSide;
    varying float paintMix;
    varying float gingerSide;
    varying vec3 carvingNormal;
    varying vec3 furCoord;
    varying vec3 anatomy;
    varying vec3 surfacePoint;
    varying vec3 coatSite;
    varying float furShade;
    varying vec4 throatSkin;
  `;
  const coatShader = `
    uniform sampler2D paint;
    uniform sampler2D fields;
    uniform sampler2D finish;
    uniform float patternMode;
    uniform float coatMark;
    uniform float isBare;
    uniform float kitten;
    uniform vec3 skinRGB;
    uniform vec3 baseRGB;
    uniform vec3 patchRGB;
    uniform float hue;
    uniform float bands;
    uniform float white;
    uniform float patternSeed;
    vec3 originalCoat(vec2 uv, float ginger) {
      vec3 color = texture2D(paint, uv).rgb;
      vec3 marks = texture2D(fields, uv).rgb;
      float fur = 1.0 - marks.g;
      // The kitten's opposite cheek uses the same profile landmarks with
      // ginger fur. Keep the iris, ear bowl and fine dark linework intact.
      float violet = smoothstep(.018,.11,(color.b-color.g)/max(.12,color.r))
        * (1.0-smoothstep(.46,.74,color.r)) * smoothstep(.025,.09,color.b);
      float grain = clamp((dot(color,vec3(.299,.587,.114))+.012)/(marks.b+.012),.75,1.10);
      // Sienna values sampled along the adult's ginger temple and brow.
      vec3 gingerPaint=mix(vec3(.64,.24,.08),vec3(.89,.49,.20),smoothstep(.09,.31,marks.b))*grain;
      color=mix(color,gingerPaint,ginger*violet);
      float orange = smoothstep(.11, .38, color.r-color.b)
        * (1.0 - smoothstep(.67, .86, color.g/max(.001,color.r)));
      float lum = dot(color, vec3(.299, .587, .114));
      float localRatio=(lum+.018)/(marks.b+.018);
      float detail=mix(clamp(localRatio,.28,.86),clamp(.88+.12*localRatio,.87,1.08),
                       smoothstep(.34,.76,localRatio));
      float seed = patternSeed - .36;
      float flow = sin(4.0*furCoord.x+2.3*furCoord.z+sin(3.0*furCoord.y)+seed*3.5);
      float originalWhite = smoothstep(.46, .54, marks.r);
      float newWhite = smoothstep(.46, .54, marks.r + (white-.55)*.93 + seed*.13*flow);
      float warm = clamp((.25-hue)*4.0,0.0,1.0);
      float cool = clamp((hue-.25)/.75,0.0,1.0);
      color = mix(color, vec3(1.09*lum+.12,.53*lum,.37*lum), warm*.90*orange*fur);
      color = mix(color, vec3(.60,.65,.77)*lum, cool*orange*fur);
      vec3 ivory = mix(vec3(.85,.75,.60), vec3(1.0,.94,.81), lum)*detail;
      vec3 copper = mix(vec3(.60,.27,.15),vec3(.90,.53,.28),lum)*detail;
      copper = mix(copper,vec3(.45,.49,.56)*lum,cool);
      float island = smoothstep(-.19,.17,flow+.51*sin(3.6*furCoord.z-2.5*furCoord.y+seed));
      float shifted = smoothstep(.015,.50,abs(seed))*island*fur*(1.0-originalWhite);
      float ink = (1.0-smoothstep(.24,.46,lum))*smoothstep(-.04,.035,color.b-color.g);
      color = mix(color,mix(vec3(.17,.15,.22),vec3(.34,.28,.38),lum)*detail,shifted*orange);
      color = mix(color,copper,shifted*ink);
      color = mix(color,ivory,max(0.0,newWhite-originalWhite)*fur);
      color = mix(color,copper,max(0.0,originalWhite-newWhite)*fur);
      float stripes = smoothstep(.20,.85,sin(furCoord.y*(20.0+bands*30.0)+furCoord.z*4.7+3.2*flow));
      float stripedArea = fur*(1.0-newWhite)*smoothstep(.09,.42,lum);
      float added = max(0.0,bands-.25)*.57 + abs(seed)*.18;
      color = mix(color,color*vec3(.53,.44,.51),stripes*added*stripedArea);
      color = mix(color,color+vec3(.15,.065,.018),max(0.0,.25-bands)*stripes*stripedArea);
      return color;
    }
    // A shallow compressed valley with a broader, lit roll on its upper bank.
    float skinPleat(float distanceToFold, float width) {
      float t=distanceToFold/width;
      float shoulder=(t-1.55)*.72;
      return .085*exp(-shoulder*shoulder)-.165*exp(-t*t);
    }
    float featureOval(vec2 point, vec2 center, vec2 radius) {
      return 1.-smoothstep(.90,1.08,length((point-center)/radius));
    }
    float bareMouth(vec2 uv) {
      // Intersect the original anatomical ink mask with the little T and W
      // of the nose and mouth. The long brushstrokes beside them are fur art.
      vec2 p=vec2(uv.x,1.-uv.y)*mix(vec2(1536.,1024.),vec2(2172.,724.),kitten);
      if(kitten<.5) {
        if(p.x>700.) return max(featureOval(p,vec2(855.,282.),vec2(19.,19.)),
                                featureOval(p,vec2(869.,307.),vec2(33.,16.)));
        return max(featureOval(p,vec2(357.,298.),vec2(27.,24.)),
                   featureOval(p,vec2(360.,322.),vec2(45.,13.)));
      }
      if(p.x>750.) return max(featureOval(p,vec2(904.,399.),vec2(27.,27.)),
                              featureOval(p,vec2(925.,437.),vec2(39.,20.)));
      return max(featureOval(p,vec2(395.,413.),vec2(33.,32.)),
                 featureOval(p,vec2(396.,449.),vec2(61.,15.)));
    }
    // Shoulder, flank and upper limb share a skin chart. A crease can
    // cross a joint without restarting at a limb's UV or paint boundary.
    vec3 trunkSkinChart(vec3 p) {
      if(kitten>.5) {
        float centerY=.28-.045*p.z;
        vec2 radial=vec2(p.x*.65,p.y-centerY);
        float dorsal=radial.y/max(length(radial),.001);
        return vec3(p.x/.65,.535+.385*dorsal,-.89+1.5*clamp((p.z+.94)/2.09,0.,1.));
      }
      float centerZ=mix(-.06,.15,smoothstep(1.1,2.07,p.y));
      vec2 radial=vec2(p.x*1.12,p.z-centerZ);
      float ventral=radial.y/max(length(radial),.001);
      return vec3(p.x/.85,.535-.385*ventral,-.89+1.5*clamp((p.y-.16)/1.91,0.,1.));
    }
    float skinFolds(vec3 chart, bool trunk) {
      float side=chart.x, h=chart.y;
      float folds=0.;
      if(trunk && kitten>.5) {
        // In the prone pose the throat is tucked between the forepaws.
        // The visible slack gathers diagonally behind the turned jaw.
        float shoulder=smoothstep(.18,.46,h)*(1.-smoothstep(.72,.94,h))
                      *smoothstep(.15,.39,abs(side));
        float rise=.21-.37*(h-.50)+.13*side;
        float lower=rise-.112+.09*(h-.50);
        folds=shoulder*(skinPleat(chart.z-rise,.021)
                    +.61*(1.-smoothstep(.50,.82,h))*skinPleat(chart.z-lower,.025));
      } else if(trunk) {
        // The throat is lower on its ventral side. Two overlapping sweeps
        // compress where the jaw meets the neck and taper into each flank.
        float throat=1.-smoothstep(.54,.82,h);
        float slope=.37*side*side+.065*side+.10*(h-.15);
        float upper=.38+slope;
        float lower=upper-.137+.12*side+.11*side*side;
        float span=1.-smoothstep(.28,.58,abs(side+.04));
        float halfSpan=1.-smoothstep(.18,.48,abs(side-.12));
        float gathered=smoothstep(.10,.34,abs(side+.05));
        folds=throat*(.43*span*skinPleat(chart.z-upper,.030)
                    +.32*gathered*halfSpan*skinPleat(chart.z-lower,.033));
      } else if(coatSite.x<1.5) {
        // The forehead folds bow over the nose, flattening as they reach
        // the temples. The upper fold is shorter and offset from the first.
        float front=smoothstep(.65,.86,anatomy.z);
        float lower=.522+.038*cos(5.7*side-.34)+.046*side;
        float upper=.610+.023*cos(5.1*side+.39)+.027*side;
        float span=1.-smoothstep(.18,.49,abs(side+.03));
        float halfSpan=1.-smoothstep(.12,.40,abs(side-.035));
        float bridge=.415+.27*abs(side+.012);
        float innerBrow=smoothstep(.016,.082,abs(side))*(1.-smoothstep(.14,.27,abs(side)));
        folds=front*(span*skinPleat(h-lower,.014)+.80*halfSpan*skinPleat(h-upper,.015)
                          +.74*innerBrow*skinPleat(h-bridge,.014));
      }
      return folds*mix(1.,.58,kitten);
    }
    vec3 coatAt(vec2 uv, float ginger) {
      vec4 brow=catBrowRegion(uv,kitten,coatSite.x);
      vec3 paintRGB=texture2D(paint,uv).rgb;
      vec3 color=paintRGB;
      float neckShadow=coatSite.x<.5 ? smoothstep(.36,.61,anatomy.z) : 0.;
      float lighting=clamp(.48+.52*dot(normalize(carvingNormal),normalize(vec3(-.48,.77,.42))),0.,1.);
      if(isBare>.5) {
        vec2 finishRG=texture2D(finish,uv).rg;
        float h=coatSite.y;
        bool head=coatSite.x>.5 && coatSite.x<1.5;
        bool trunk=!head && coatSite.x<2.5;
        vec3 chart=trunk ? trunkSkinChart(surfacePoint) : vec3(anatomy.x,h,anatomy.z);
        if(kitten>.5)neckShadow=trunk ? smoothstep(.36,.61,chart.z) : 0.;
        else {
          // A soft occlusion under the chin crosses both sides of the jaw
          // boundary. The nape receives just the continuous surface light.
          vec2 underJaw=vec2(surfacePoint.x/.34,(surfacePoint.y-2.003-.18*surfacePoint.x*surfacePoint.x)/.074);
          neckShadow=.75*exp(-dot(underJaw,underJaw))*smoothstep(.15,.53,surfacePoint.z);
        }
        float toes=coatSite.x>1.5 && coatSite.x<2.5 ? 1.-smoothstep(.095,.22,h) : 0.;
        // The paw clefts are useful anatomical ink. The brush marks on the
        // rest of the original sheet follow fur clumps, so use skin relief there.
        float toeCleft=min(1.,.60+.80*finishRG.r);
        float detail=mix(1.,toeCleft,toes*.72);
        float facing=smoothstep(.65,.95,anatomy.z);
        float cheek=abs(anatomy.x);
        vec2 pad=vec2((cheek-.21)/.33,(h-.16)/.145);
        float muzzle=head ? facing*exp(-dot(pad,pad)) : 0.;
        vec2 underEye=vec2((cheek-.34)/.24,(h-.31)/.12);
        float lidPlane=head ? facing*exp(-dot(underEye,underEye)) : 0.;
        float ventralDepth=mix(.29*surfacePoint.y-.46*surfacePoint.z+.04,
                               .92*surfacePoint.y-.23*surfacePoint.z+.10,kitten);
        float underside=1.-smoothstep(.13,.70,trunk ? ventralDepth : coatSite.z);
        vec3 skin=mix(skinRGB,vec3(.98,.87,.77),.36*muzzle+.13*lidPlane+.065*underside);
        color=skin*detail*(.83+.27*lighting)*(1.+skinFolds(chart,trunk));

        // Restore the registered ear, nose and fine facial ink. The lighter
        // reds pick up the individual's undertone, while the dark lines stay crisp.
        float value=dot(paintRGB,vec3(.299,.587,.114));
        float warmPigment=smoothstep(.15,.38,value)*smoothstep(.05,.24,paintRGB.r-paintRGB.b);
        float nose=head ? facing*(1.-smoothstep(.13,.22,cheek))*(1.-smoothstep(.28,.38,h)) : 0.;
        vec3 flush=mix(skinRGB,vec3(.86,.43,.41),.44+.32*nose)*clamp(.47+1.02*value,.48,1.18);
        vec3 features=mix(paintRGB,flush,warmPigment*.84);
        float pinna=smoothstep(.51,.68,h)*smoothstep(.21,.41,cheek)*smoothstep(.13,.26,value);
        float lips=facing*bareMouth(uv);
        float landmarks=head ? max(pinna,lips) : 0.;
        color=mix(color,features,finishRG.g*landmarks);
        if(head && kitten>.5 && throatSkin.w>.001) {
          // The tucked kitten's cheek can lie directly on the breast. The
          // small contact patch inherits the same skin and shadow on both sides.
          vec3 chestChart=trunkSkinChart(throatSkin.xyz);
          float depth=.92*throatSkin.y-.23*throatSkin.z+.10;
          vec3 chest=mix(skinRGB,vec3(.98,.87,.77),.065*(1.-smoothstep(.13,.70,depth)));
          color=mix(color,chest*(.83+.27*lighting)*(1.+skinFolds(chestChart,true)),throatSkin.w);
          neckShadow=mix(neckShadow,smoothstep(.36,.61,chestChart.z),throatSkin.w);
        }
      } else {
        if(patternMode<.5) color=originalCoat(uv,ginger);
        if(patternMode>.5 || coatMark>.5) {
          vec2 finishRG=texture2D(finish,uv).rg;
          color=patternCoat(color,anatomy,surfacePoint,.60+.80*finishRG.r,finishRG.g,
            vec4(patternMode,patternSeed,bands,white),vec4(coatMark,coatSite.xy,lighting),baseRGB,patchRGB,coatSite.z);
        }
      }
      if(patternMode>.5 || isBare>.5) {
        // Cool shadow planes separate the chin and breast even on white fur.
        color*=mix(vec3(.77,.80,.87),vec3(1.04,1.02,1.),lighting);
        color*=1.-.12*neckShadow;
      }
      color=catBrowColor(color,brow,isBare);
      color=catLeather(color,paintRGB,uv,paintedFront,coatSite.x,kitten);
      return catEyeColor(color,paintRGB,uv,paintedFront);
    }
  `;
  function surface(T, genes, atlas) {
    const material = new T.MeshBasicMaterial({side:T.DoubleSide});
    material.extensions = {derivatives:true};
    material.defaultAttributeValues = {profileReach:[0],registeredSideUV:[0,0],mirroredGinger:[0],fiberShade:[0],throatContact:[0,0,0,0]};
    material.onBeforeCompile = shader => {
      shader.uniforms.paint = {value:atlas.paint};
      shader.uniforms.fields = {value:atlas.fields};
      shader.uniforms.finish = {value:atlas.finish};
      shader.uniforms.kitten = {value:genes.age === "kitten" ? 1 : 0};
      shader.uniforms.isBare = {value:genes.furLength==="hairless" ? 1 : 0};
      shader.uniforms.patternMode = {value:genes.coatPattern};
      shader.uniforms.coatMark = {value:genes.marking};
      shader.uniforms.baseRGB = {value:pigment(T,window.CatCoat.palette,genes.baseColor)};
      shader.uniforms.patchRGB = {value:pigment(T,window.CatCoat.palette,genes.patchColor)};
      shader.uniforms.skinRGB = {value:pigment(T,skins,genes.skinColor)};
      for (const trait of ["hue","bands","white","patternSeed"]) {
        shader.uniforms[trait] = {value:genes[trait]};
      }
      shader.vertexShader = shader.vertexShader
        .replace("#include <common>", "#include <common>\nuniform float kitten; uniform float isBare; attribute vec2 frontUV; attribute vec2 sideUV; attribute vec2 registeredSideUV; attribute float viewMix; attribute float profileReach; attribute float mirroredGinger; attribute vec3 coatDomain; attribute vec3 pigmentDomain; attribute vec3 coatRegion; attribute float fiberShade; attribute vec4 throatContact;\n"+varying)
        .replace("#include <begin_vertex>", `#include <begin_vertex>
          paintedFront=frontUV; paintedSide=sideUV;
          throatSkin=throatContact;
          anatomy=coatDomain; surfacePoint=isBare>.5 ? position : pigmentDomain; coatSite=coatRegion; furShade=fiberShade;
          vec3 chart=vec3(frontUV.x,frontUV.y,sideUV.x);
          furCoord=mix((chart-vec3(.23,.05,.55))*vec3(5.2,1.85,4.1),
                       (chart-vec3(.19,.10,.41))*vec3(5.8,1.85,3.6),kitten);
          vec3 toEye=vec3(modelViewMatrix[0].z,modelViewMatrix[1].z,modelViewMatrix[2].z);
          float az=atan(toEye.x,toEye.z);
          gingerSide=mirroredGinger;
          az=mix(az,-az,mirroredGinger);
          if (registeredSideUV.x>0.) paintedSide=mix(registeredSideUV,sideUV,smoothstep(1.20,1.53,az));
          paintMix=viewMix+profileReach*smoothstep(.58,1.35,az)*(1.-smoothstep(1.95,2.64,az));
          carvingNormal=mat3(modelMatrix)*normal;`);
      shader.fragmentShader = shader.fragmentShader
        .replace("#include <common>", "#include <common>\n"+varying+window.CatCoat.glsl+window.CatIris.glsl+
          window.CatPigment.glsl+window.CatBrows.glsl+coatShader)
        .replace("#include <color_fragment>", `#include <color_fragment>
          vec3 srgb=clamp((1.+furShade)*mix(coatAt(paintedFront,0.0),coatAt(paintedSide,gingerSide),smoothstep(.15,.85,paintMix)),0.,1.);
          float relief=.975+.045*dot(normalize(carvingNormal),normalize(vec3(-.4,.7,.55)));
          diffuseColor.rgb=mix(srgb/12.92,pow((srgb+.055)/1.055,vec3(2.4)),step(vec3(.04045),srgb))*relief;`);
      window.CatIris.bind(T,shader,genes);
      window.CatPigment.bind(T,shader,genes,atlas);
      window.CatBrows.bind(T,shader,genes);
    };
    return material;
  }
  function carving(T, genes, kit) {
    const root = new T.Group();
    const kitten = genes.age === "kitten";
    root.rotation.y = -Math.PI/2;
    window.WoodBody.build(T, root, genes, kit);
    const headBuilder = kitten ? window.KittenHead : window.WoodHead;
    const head = headBuilder.build(T, root, genes, kit);
    if (kitten) {
      // The chest stays between the forepaws as the kitten looks up from them.
      const neck = new T.Vector3(0, .46, .82);
      head.rotation.y = kittenHeadTurn;
      head.position.copy(neck).sub(neck.clone().applyQuaternion(head.quaternion));
      if(genes.furLength==="hairless") window.KittenHead.blendThroat(T,root,head);
    }
    root.name = kitten ? "Prone painted kitten" : "Seated painted cat";
    return root;
  }
  function build(T, expression) {
    const genes = {...defaults,...expression};
    const stage = genes.age==="kitten" ? "kitten" : "adult";
    const kit = {material:surface(T,genes,atlases[stage])};
    const root = carving(T,genes,kit);
    // Bare cheeks and throat have their own surface. Register folds to its
    // neutral pose so editing a face or switching coats never moves the folds.
    const bare = genes.furLength==="hairless";
    const geometryKey = bare ? stage+"-skin" : stage;
    const needNeutral = !window.CatCoordinates.has(geometryKey) &&
      ["fullness","cheeks","muzzle","ears","eyes"].some(key=>genes[key]!==defaults[key]);
    const neutral = needNeutral ? carving(T,{...defaults,age:stage,furLength:bare?"hairless":"short"},kit) : root;
    window.CatCoordinates.attach(T,root,stage,neutral,geometryKey);
    if (needNeutral) neutral.traverse(mesh=>{if(mesh.isMesh) mesh.geometry.dispose();});
    window.WoodHead.foldEars(T,root,genes);
    if(stage==="adult") {
      window.CatHeadVolume.apply(T,root,genes);
      if(bare)window.WoodHead.finishEarSkin(T,root);
    }
    if (bare) window.CatBareBody.join(T,root,genes);
    window.CatFur.apply(T,root,genes,kit);
    return root;
  }
  function dispose() {
    window.CatBrows.dispose();
    for (const atlas of Object.values(atlases)) {
      atlas.paint.dispose();
      atlas.fields.dispose();
      atlas.finish.dispose();
      atlas.leather.dispose();
    }
  }
  window.CatModel = {prepare,build,dispose,kittenHeadTurn,defaults,skins};
})();
