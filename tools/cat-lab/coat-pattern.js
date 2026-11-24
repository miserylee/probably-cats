/* Paint is evaluated in the cat's rest coordinates. Rotating the camera or
   changing the proportions therefore never reseeds the patches. */
(() => {
  "use strict";
  const modes = [
    {value:0, label:"原画三花"}, {value:1, label:"纯色"},
    {value:2, label:"渐层"}, {value:3, label:"虎斑"},
    {value:4, label:"三花"}, {value:5, label:"随机拼色"}
  ];
  const markings = [
    {value:0, label:"无"}, {value:1, label:"白手套"},
    {value:2, label:"乌云盖雪"}, {value:3, label:"白围脖"},
    {value:4, label:"重点色"}, {value:5, label:"梵花"},
    {value:6, label:"高白斑"}, {value:7, label:"斑点虎斑"}
  ];
  const palette = [
    {value:"ginger", label:"暖橘", hex:"#C8773E"},
    {value:"cream", label:"奶油", hex:"#E7CB9E"},
    {value:"chalk", label:"象牙白", hex:"#F3ECE0"},
    {value:"fawn", label:"浅鹿棕", hex:"#C1A18C"},
    {value:"cinnamon", label:"肉桂", hex:"#A96242"},
    {value:"chocolate", label:"巧克力", hex:"#795245"},
    {value:"blue", label:"蓝灰", hex:"#7E8997"},
    {value:"silver", label:"银灰", hex:"#C1C5CA"},
    {value:"lilac", label:"丁香灰", hex:"#AE9CA5"},
    {value:"charcoal", label:"炭黑", hex:"#403D48"},
    {value:"black", label:"墨黑", hex:"#292831"}
  ];

  const glsl = `
    float coatHash(vec3 point) {
      vec3 q = fract(point * vec3(.1031,.1030,.0973));
      q += dot(q, q.yxz + 33.33);
      return fract((q.x + q.y) * q.z);
    }

    float coatNoise(vec3 point) {
      vec3 cell=floor(point), t=fract(point);
      t=t*t*(3.0-2.0*t);
      float lower=mix(mix(coatHash(cell),coatHash(cell+vec3(1,0,0)),t.x),
                      mix(coatHash(cell+vec3(0,1,0)),coatHash(cell+vec3(1,1,0)),t.x),t.y);
      float upper=mix(mix(coatHash(cell+vec3(0,0,1)),coatHash(cell+vec3(1,0,1)),t.x),
                      mix(coatHash(cell+vec3(0,1,1)),coatHash(cell+vec3(1,1,1)),t.x),t.y);
      return mix(lower,upper,t.z);
    }

    // Sparse, broad patches with a few nicks like the edges of the painted ruff.
    float coatIslands(vec3 p, vec3 shift) {
      vec3 cell=p*vec3(1.35,2.0,1.7)+shift;
      vec3 ripple=vec3(sin(cell.z*2.1+cell.y),cos(cell.x*2.4-cell.z),sin(cell.y*2.3+cell.x));
      float lobes=sin(dot(p,vec3(2.31,4.65,4.97))+shift.x+1.5*ripple.x);
      return .11+.18*lobes+.67*coatNoise(cell+.16*ripple)+.11*coatNoise(cell*3.6+vec3(3.9,1.6,7.2));
    }

    float coatBand(float phase,float width) {
      float d=abs(fract(phase+.5)-.5);
      float aa=clamp(fwidth(phase)*.62,.012,.115);
      return 1.0-smoothstep(width-aa,width+aa,d);
    }

    float coatTabby(vec3 p, float part, float height, float density, vec3 shift) {
      float x=abs(p.x), count=mix(2.25,5.85,density);
      float curve=.47*sin(p.z*4.7+shift.y)+.16*sin(p.z*10.0+shift.z);
      float deviation=.24*coatNoise(p*3.2+shift);
      float phase=p.z*count + curve*(1.0-p.y) + .58*x + .34*p.y + deviation;
      float stripe=coatBand(phase,.08+.035*sin(p.z*3.4+shift.z));
      if(part<.5) {
        float spine=(1.0-smoothstep(.035,.14,x))*smoothstep(.52,.78,height)*
                    (1.0-smoothstep(-.14,.20,p.z));
        // Some stripes taper into the light lower flank instead of forming a cage.
        float end=smoothstep(.06,.35,height+.11*sin(p.z*9.0+shift.x));
        stripe=max(.87*spine,stripe*end)*(.38+.62*smoothstep(.10,.61,x+.26*(.5-p.z)));
      } else if(part<1.5) {
        // A pair of forked pencil strokes on the forehead and two cheek bars.
        float center=.13+.46*abs(height-.63);
        float forehead=(1.0-smoothstep(.022,.075,abs(x-center)))*
                       smoothstep(.37,.53,height)*(1.0-smoothstep(.80,.97,height));
        float temples=(1.0-smoothstep(.026,.085,abs(x-.44-.32*(height-.58))))*
                      smoothstep(.48,.62,height)*(1.0-smoothstep(.79,.95,height));
        float cheek=coatBand(height*4.6+x*.71+p.z*.20,.095)*
                    smoothstep(.36,.60,x)*(1.0-smoothstep(.45,.56,height));
        stripe=max(max(forehead,temples*.77),cheek);
      } else if(part<2.5) {
        float legRings=coatBand(height*(2.1+density*1.85)+.19*x+.06*sin(p.z*8.0+shift.z),.087);
        stripe=mix(.87*legRings,stripe*.64,smoothstep(.52,.90,height))*smoothstep(.035,.17,height);
      } else {
        // Follow the tail from its base to its tip, including curled seated tails.
        stripe=coatBand(height*(3.2+3.8*density)+.07*sin(p.x*5.0+shift.x),.17);
      }
      return stripe;
    }

    float pointDepth(vec3 p, float part, float h) {
      if(part<.5) return 0.;
      if(part<1.5) {
        // Center the mask on the muzzle and leave the crown between the ears light.
        vec2 face=vec2(p.x/.78,(h-.24)/.31);
        float mask=(1.-smoothstep(.60,1.32,length(face)))*smoothstep(.58,.86,p.z);
        float ear=smoothstep(.51,.76,h)*smoothstep(.20,.45,abs(p.x));
        return max(mask,ear);
      }
      if(part<2.5) return 1.-smoothstep(.13,mix(.46,.74,step(-.1,p.z)),h);
      return .84+.16*smoothstep(0.,.45,h);
    }

    float piebaldPatch(vec3 q,vec3 center,vec3 radius,float edge) {
      float d=length((q-center)/radius);
      return 1.-smoothstep(.95,1.05,d+edge);
    }

    float highWhitePigment(vec3 p,vec3 islands,float part,float h,vec3 shift,
                           float white,bool van) {
      float flip=shift.z>15.5 ? 1. : -1.;
      float rag=.12*(coatNoise(islands*8.+shift)-.5);
      if(part>2.5) return 1.;
      if(part>.5 && part<1.5) {
        float side=p.x*flip;
        if(van) {
          float blaze=.075+.08*white+.035*sin(h*7.+shift.x);
          float crown=smoothstep(.38,.46,h+.08*side+rag)*
                      (1.-smoothstep(.72,.85,h+rag));
          return smoothstep(blaze-.018,blaze+.018,abs(side+.035)+rag)*crown;
        }
        // One eye sits inside the asymmetric cap. The opposite cheek stays white.
        float edge=.09+.45*(white-.5)-.52*(h-.4)+rag;
        return smoothstep(edge-.025,edge+.025,side)*smoothstep(.18,.24,h+rag);
      }
      if(van) return 0.;
      // One pigment volume crosses the trunk and haunch so the edge of a
      // flank patch can land on either mesh. The lower chest and feet stay white.
      vec3 q=vec3(islands.x*flip,islands.yz);
      float size=mix(1.14,.87,white);
      vec3 drift=.11*(fract(shift*.63)-.5);
      float shoulder=piebaldPatch(q,vec3(.54,.54,.26)+drift,vec3(.65,.39,.39)*size,rag);
      float flank=piebaldPatch(q,vec3(-.62,.52,-.25)-drift,vec3(.67,.45,.47)*size,rag);
      float rump=piebaldPatch(q,vec3(.24,.51,-.69)-drift.zyx,vec3(.53,.36,.33)*size,rag);
      float color=max(shoulder,max(flank,rump))*smoothstep(.14,.30,islands.y);
      if(part>1.5) color*=smoothstep(.42,.75,h);
      return color;
    }

    float spottedTabby(vec3 p,vec3 islands,float part,float h,float density,vec3 shift) {
      if((part>.5 && part<1.5) || part>2.5)
        return coatTabby(p,part,h,density,shift);
      // An oblique lattice of ellipsoids intersects each part of the carving.
      // The spots stay round on its chest as well as its broadside.
      vec3 q=islands*vec3(3.8,8.6,4.1)*mix(.86,1.27,density);
      q.xz=mat2(.91,.41,-.41,.91)*q.xz;
      q.xy=mat2(.94,-.34,.34,.94)*q.xy;
      q+=shift+.13*sin(q.zxy*1.6);
      vec3 cell=floor(q);
      vec3 jitter=vec3(coatHash(cell),coatHash(cell+17.),coatHash(cell+32.));
      vec3 center=.5+.13*(jitter-.5);
      vec3 delta=(fract(q)-center)/mix(vec3(.32,.29,.30),vec3(.43,.41,.43),jitter);
      float radius=length(delta);
      float aa=clamp(fwidth(radius)*.6,.08,.30);
      float spot=1.-smoothstep(1.-aa,1.+aa,radius);
      if(part>1.5) {
        float bars=coatTabby(p,part,h,density,shift);
        spot=mix(bars,spot,smoothstep(.35,.73,h));
      }
      return spot;
    }

    vec3 patternCoat(vec3 paintRGB, vec3 p, vec3 islands, float grain, float protection,
                     vec4 coatA, vec4 coatB, vec3 baseRGB, vec3 patchRGB, float dorsalLevel) {
      float mode=coatA.x, mark=coatB.x, part=coatB.y, h=coatB.z;
      if(mode<.5 && mark<.5) return paintRGB;
      float seed=floor(coatA.y*65535.0+.5);
      vec3 shift=vec3(coatHash(vec3(seed,3,7)),coatHash(vec3(seed,8,2)),
                      coatHash(vec3(seed,1,9)))*31.0;
      float white=coatA.w, x=abs(p.x);
      vec3 ivory=vec3(.97,.93,.84);
      vec3 pigment=baseRGB;
      if(mode>1.5 && mode<2.5) {
        // A single rest-pose plane crosses the limbs, rump and head. Dark
        // tips build towards the back without restarting at a mesh seam.
        float flecks=coatNoise(p*vec3(15.,24.,14.)+shift)-.5;
        float tips=smoothstep(.13,.99,dorsalLevel+.065*flecks);
        pigment=mix(baseRGB,patchRGB,.86*tips);
      } else if(mode>2.5 && mode<3.5) {
        float stripe=coatTabby(p,part,h,coatA.z,shift);
        vec3 bandColor=mix(baseRGB*.45,patchRGB,.73);
        pigment=mix(baseRGB,bandColor,stripe*.9);
      } else if(mode>3.5) {
        float field=coatIslands(islands,shift);
        float boundary=mode<4.5 ? .50 : .48;
        float edge=clamp(fwidth(field)*.70,.006,.055);
        float second=smoothstep(boundary-edge,boundary+edge,field);
        pigment=mix(baseRGB,patchRGB,second);
        if(mode<4.5) {
          float whiteField=coatIslands(islands.zxy,shift.yzx+vec3(9,14,2));
          // Keep the asymmetric islands, with a bias for a white belly and feet.
          whiteField+=.19*(.46-islands.y);
          float threshold=mix(.67,.43,white);
          float fade=clamp(fwidth(whiteField)*.70,.007,.055);
          pigment=mix(pigment,ivory,smoothstep(threshold-fade,threshold+fade,whiteField));
        }
      }

      float light=mix(.82,1.10,clamp(coatB.w,0.0,1.0));
      // Preserve the brush marks and tiny dark hairlines, independent of their
      // original color fields. The original illustration is already lit.
      float detail=mix(1.0,clamp(grain,.66,1.12),.71);
      if(part>.5 && part<1.5 && h<.55) {
        // Ink from the whiskers and mouth survives recoloring. A smooth dark
        // cheek patch has almost no high-frequency contrast of its own.
        float inkLine=1.0-smoothstep(.28,.65,grain);
        detail=mix(detail,clamp(grain,.13,1.0),inkLine);
      }
      float relief=detail*light;
      vec3 coat=mode<.5 ? paintRGB : pigment*relief;
      if(mark>.5 && mark<3.5) {
        float wobble=.024*(coatNoise(p*4.2+shift)-.5);
        float glove=0.0;
        if(part>1.5 && part<2.5) {
          // The hind paw tucks under the seated cat. Keep white on its toes
          // so the outer thigh never reads as a second white sock.
          float front=step(-.1,p.z);
          float cuff=mix(mix(.045,.14,white),mix(.13,.32,white),front);
          cuff+=wobble+.017*sin(3.0*p.x+shift.x);
          glove=1.0-smoothstep(cuff-.023,cuff+.023,h);
        }
        float amount=glove;
        if(mark>1.5 && mark<2.5) {
          // Continue the ventral white from the chin through the chest and
          // belly with the same physical plane used by the shaded coat.
          float snowline=.40+.17*(white-.5)+.043*(1.0-min(x,1.0))+wobble;
          float snow=1.0-smoothstep(snowline-.019,snowline+.019,dorsalLevel);
          if(part>2.5) snow=0.0;
          amount=max(glove,snow);
          float darkFacet=dot(mode<.5?paintRGB:pigment,vec3(.30,.59,.11));
          coat=mix(vec3(.13,.13,.16),vec3(.26,.25,.30),darkFacet)*relief;
        } else if(mark>2.5) {
          float chest=smoothstep(.01,.48,p.z);
          float bibWidth=mix(.34,.80,h)+.16*(white-.5);
          float bib=chest*(1.0-smoothstep(bibWidth-.035,bibWidth+.035,x+wobble))*
                    smoothstep(.14,.43,h);
          if(part>.5 && part<1.5) bib=(1.0-smoothstep(.18,.25,h+wobble))*(1.0-smoothstep(.64,.85,x));
          if(part>1.5) bib=0.0;
          amount=bib;
        }
        coat=mix(coat,ivory*relief,amount);
      } else if(mark>3.5 && mark<4.5) {
        float depth=pointDepth(p,part,h);
        vec3 warmBody=mix(ivory,baseRGB,.29);
        vec3 points=mix(baseRGB*.43,patchRGB*.73,.75);
        coat=mix(warmBody,points,depth)*relief;
      } else if(mark>4.5 && mark<6.5) {
        float color=highWhitePigment(p,islands,part,h,shift,white,mark<5.5);
        // The hand-painted coat already includes large white regions. Use its
        // selected pigment here so the head patches and tail read through.
        vec3 colored=mode<.5 ? baseRGB*relief : coat;
        coat=mix(ivory*relief,colored,color);
      } else if(mark>6.5) {
        float spots=spottedTabby(p,islands,part,h,coatA.z,shift);
        vec3 ink=mix(baseRGB*.35,patchRGB*.78,.68);
        vec3 ground=mode<.5 ? baseRGB : pigment;
        coat=mix(ground,ink,spots)*relief;
      }
      return mix(clamp(coat,0.0,1.0),paintRGB,clamp(protection,0.0,1.0));
    }
  `;
  window.CatCoat={modes,markings,palette,glsl};
})();
