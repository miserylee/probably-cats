/* Eyelid silhouettes in the two registered paint sheets, in source pixels.
   Each station gives x, upper lid y, lower lid y, including the brush line. */
(() => {
  "use strict";
  const lids = {
    adult: [
      [[217,242,242],[234,239,248],[246,234,260],[258,231,268],[271,230,272],
       [283,231,274],[292,235,274],[301,240,273],[309,249,272],[315,258,272],[322,272,272]],
      [[384,262,262],[389,247,261],[394,234,260],[402,223,259],[410,216,258],
       [422,211,255],[434,208,250],[446,208,241],[454,209,230],[458,209,222],
       [463,210,215],[473,208,208]],
      [[905,271,271],[914,254,263],[919,237,260],[925,226,259],[932,220,258],
       [941,216,255],[953,214,250],[965,214,241],[976,215,231],[987,216,222],[997,217,217]]
    ],
    kitten: [
      [[250,341,341],[268,335,347],[278,330,359],[288,325,370],[300,322,377],
       [311,320,380],[322,319,381],[332,321,379],[339,326,377],[346,337,375],
       [352,350,374],[357,362,375],[360,374,375]],
      [[432,376,376],[437,357,378],[444,338,379],[451,326,380],[460,320,381],
       [472,315,379],[482,313,375],[492,313,367],[498,315,358],[504,317,344],
       [509,318,329],[516,321,324],[529,326,326]],
      [[978,382,382],[987,374,380],[993,359,380],[995,328,380],[1002,314,379],
       [1012,308,377],[1023,308,374],[1034,313,371],[1044,318,365],[1054,326,357],
       [1064,334,349],[1074,335,343],[1084,337,339],[1089,337,337]]
    ]
  };
  const pigments = {
    original: ["原画",null], amber: ["琥珀","#E4A24B"], gold: ["金黄","#EAC965"],
    copper: ["铜棕","#C27B4F"], hazel: ["榛褐","#AE9763"], green: ["绿","#90AE70"],
    aqua: ["青绿","#79B6AA"], blue: ["蓝","#7EAFD0"]
  };
  const palette = Object.fromEntries(Object.entries(pigments).map(([key,[name]])=>[key,name]));
  function uniformValue(T, key) {
    const hex = pigments[key]?.[1];
    if (!hex) return new T.Vector4(0,0,0,0);
    const rgb = parseInt(hex.slice(1),16);
    // The paint shader blends in sRGB before the renderer's final conversion.
    return new T.Vector4((rgb>>16)/255,(rgb>>8&255)/255,(rgb&255)/255,1);
  }
  function bind(T, shader, genes) {
    shader.uniforms.irisLeft = {value:uniformValue(T,genes.eyeLeft)};
    shader.uniforms.irisRight = {value:uniformValue(T,genes.eyeMode==="odd" ? genes.eyeRight : genes.eyeLeft)};
    shader.uniforms.irisKitten = {value:genes.age==="kitten" ? 1 : 0};
    shader.vertexShader = shader.vertexShader
      .replace("#include <common>","#include <common>\nvarying float irisHeight;")
      .replace("#include <begin_vertex>","#include <begin_vertex>\nirisHeight=position.y;");
  }
  // Generate straight-line GLSL for the contour interpolation. Most fragments
  // leave at the UV bounds, before any individual eyelid is evaluated.
  function contourFunction(age,index,stations) {
    const name=age+"Lid"+index;
    const pair=([,top,bottom])=>"vec2("+top+".0,"+bottom+".0)";
    let body="";
    for(let i=1;i<stations.length;i++){
      const a=stations[i-1], b=stations[i], limit=b[0];
      body+="if(x<="+limit+".0)return mix("+pair(a)+","+pair(b)+",(x-"+a[0]+".0)/"+(limit-a[0])+".0);\n";
    }
    return "vec2 "+name+"(float x){\n"+body+"return "+pair(stations[stations.length-1])+";}\n";
  }
  function stageFunction(age,eyes) {
    const lo=Math.min(...eyes.flatMap(eye=>eye.map(p=>p[1])))-2;
    const hi=Math.max(...eyes.flatMap(eye=>eye.map(p=>p[2])))+2;
    let body="if(p.y<"+lo+".0||p.y>"+hi+".0)return 0.0;\n";
    for(let i=0;i<eyes.length;i++){
      const stations=eyes[i], first=stations[0][0], last=stations[stations.length-1][0];
      body+="if(p.x>="+first+".0&&p.x<="+last+".0)return lidCoverage(p.y,"+age+"Lid"+i+"(p.x));\n";
    }
    return eyes.map((stations,i)=>contourFunction(age,i,stations)).join("")+
      "float "+age+"Eye(vec2 p){\n"+body+"return 0.0;}\n";
  }
  const glsl=`
    uniform vec4 irisLeft;
    uniform vec4 irisRight;
    uniform float irisKitten;
    varying float irisHeight;
    float lidCoverage(float y, vec2 lids) {
      float distanceToEdge=min(y-lids.x,lids.y-y);
      return smoothstep(-.65,.65,distanceToEdge);
    }
  `+stageFunction("adult",lids.adult)+stageFunction("kitten",lids.kitten)+`
    vec3 catEyeColor(vec3 coat, vec3 paintRGB, vec2 uv, vec2 restFrontUV) {
      // Guard by physical skull height as well: fur at the ear and a few
      // decorative whisker ribbons borrow ink swatches from the eyelid art.
      vec2 orbit=mix(vec2(2.25,2.65),vec2(.54,.78),irisKitten);
      if(irisHeight<orbit.x || irisHeight>orbit.y) return coat;
      vec2 sheet=mix(vec2(1536.,1024.),vec2(2172.,724.),irisKitten);
      vec2 front=vec2(restFrontUV.x,1.0-restFrontUV.y)*sheet;
      float left=step(mix(352.,395.,irisKitten),front.x);
      vec2 eyeCenter=mix(mix(vec2(274.,250.),vec2(422.,235.),left),
                         mix(vec2(310.,349.),vec2(479.,347.),left),irisKitten);
      vec2 socketRadius=mix(vec2(106.,48.),vec2(118.,59.),irisKitten);
      if(any(greaterThan(abs(front-eyeCenter),socketRadius))) return coat;
      vec2 pixel=vec2(uv.x,1.0-uv.y)*sheet;
      float opening=irisKitten>.5 ? kittenEye(pixel) : adultEye(pixel);
      if (opening<=0.) return coat;
      // A cat's left eye appears on the right of its front portrait. The
      // rest-pose front UV identifies it even when both sides use one profile.
      vec4 pigment=mix(irisRight,irisLeft,left);
      float chroma=paintRGB.r-paintRGB.b;
      float iris=smoothstep(.19,.44,paintRGB.r)*smoothstep(.08,.24,paintRGB.g)
        *smoothstep(.075,.28,chroma)*(1.-smoothstep(.50,.71,paintRGB.b/max(.04,paintRGB.r)));
      float luminance=dot(paintRGB,vec3(.299,.587,.114));
      float light=clamp((luminance-.10)/.63,0.,1.11);
      vec3 dyed=pigment.rgb*(.23+.78*light);
      vec3 eye=mix(paintRGB,dyed,iris*pigment.a);
      return mix(coat,eye,opening);
    }
  `;
  const swatches = Object.fromEntries(Object.entries(pigments).map(([key,[,hex]])=>[key,hex]));
  window.CatIris={glsl,bind,palette,swatches};
})();
