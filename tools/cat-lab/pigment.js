/* Ear bowls and nose leather share the coat's registered views. Nose flecks
   use the same frontal coordinates from either view, including in profile. */
(() => {
  "use strict";
  const ears = [
    {value:"auto",label:"随毛色或肤色"}, {value:"rose",label:"蔷薇粉"},
    {value:"smoke",label:"烟灰"}, {value:"dark",label:"墨黑"}
  ];
  const noses = [
    {value:"auto",label:"随毛色或肤色"}, {value:"rose",label:"粉鼻"},
    {value:"spotted",label:"粉底黑斑"}, {value:"dark",label:"黑鼻"}
  ];

  function bind(T,shader,genes,atlas) {
    const bare = genes.furLength==="hairless";
    const shade = bare ? ({slate:.65,cocoa:.45}[genes.skinColor] || 0) :
      genes.coatPattern===0 && genes.marking<4 ? 0 :
      ({black:1,charcoal:.87,chocolate:.51,blue:.42,silver:.15}[genes.baseColor] || 0);
    const ear = {auto:shade,rose:0,smoke:.67,dark:1}[genes.earPigment] ?? shade;
    const nose = {auto:shade,rose:0,spotted:0,dark:1}[genes.nosePigment] ?? shade;
    const solid = !bare && genes.coatPattern===1 && genes.marking===0 && genes.baseColor==="black";
    shader.uniforms.leatherMap = {value:atlas.leather};
    shader.uniforms.leatherExpression = {value:new T.Vector4(ear,nose,genes.nosePigment==="spotted"?1:0,solid?1:0)};
    shader.uniforms.overrideRose = {value:new T.Vector2(genes.earPigment==="rose"?1:0,
      ["rose","spotted"].includes(genes.nosePigment)?1:0)};
    shader.uniforms.noseSeed = {value:genes.noseSeed};
  }

  const glsl = `
    uniform sampler2D leatherMap;
    uniform vec4 leatherExpression;
    uniform vec2 overrideRose;
    uniform float noseSeed;
    float noseFleck(vec2 p, vec2 center, vec2 radius) {
      vec2 local=(p-center)/radius;
      float d=length(local)+.075*sin(local.x*4.+local.y*2.)*sin(local.y*3.5);
      float aa=clamp(fwidth(d)*.64,.045,.48);
      return 1.-smoothstep(1.-aa,1.+aa,d);
    }
    vec3 catLeather(vec3 coat, vec3 ink, vec2 uv, vec2 front, float part, float baby) {
      if(leatherExpression.w>.5) {
        float inkValue=dot(coat,vec3(.30,.59,.11));
        coat=mix(vec3(.018,.017,.020),vec3(.24,.24,.26),clamp(inkValue/.25,0.,1.));
      }
      if(part<.5 || part>1.5) return coat;
      if(dot(leatherExpression.xyz,vec3(1.))+dot(overrideRose,vec2(1.))<.001) return coat;
      vec2 cover=texture2D(leatherMap,uv).rg;
      if(max(cover.x,cover.y)<.001) return coat;
      float value=dot(ink,vec3(.30,.59,.11));
      float relief=smoothstep(.075,.86,value);
      vec3 earBlack=mix(vec3(.055,.051,.062),vec3(.245,.227,.241),relief);
      vec3 noseBlack=mix(vec3(.037,.031,.039),vec3(.225,.198,.217),relief);
      vec3 earRose=mix(vec3(.31,.14,.16),vec3(.94,.65,.58),relief);
      vec3 noseRose=mix(vec3(.34,.13,.16),vec3(.95,.59,.56),relief);
      vec3 pinna=mix(mix(coat,earRose,overrideRose.x),earBlack,leatherExpression.x);
      vec3 nose=mix(mix(coat,noseRose,overrideRose.y),noseBlack,leatherExpression.y);
      if(leatherExpression.z>.5 && cover.y>.001) {
        vec2 sheet=mix(vec2(1536.,1024.),vec2(2172.,724.),baby);
        vec2 p=(vec2(front.x,1.-front.y)*sheet-mix(vec2(356.,293.),vec2(394.,408.),baby))
          /mix(vec2(22.,14.),vec2(31.,18.),baby);
        float seed=floor(noseSeed*65535.+.5);
        float a=coatHash(vec3(seed,17.,13.)), b=coatHash(vec3(seed,5.,29.));
        p.x*=a>.5 ? -1. : 1.;
        float spots=noseFleck(p,vec2(-.61+.43*b,-.27+.28*a),vec2(.34+.23*a,.38+.18*b));
        spots=max(spots,noseFleck(p,vec2(.19+.33*a,-.06+.34*b),vec2(.11+.14*b,.15+.08*a)));
        nose=mix(nose,noseBlack,spots);
      }
      return mix(mix(coat,pinna,cover.x),nose,cover.y);
    }
  `;
  window.CatPigment={ears,noses,glsl,bind};
})();
