/* A fixed pose, sculpted in sections. Each broad plane carries the same
   continuous coat coordinates as its neighbours, even at the pigment edges. */
(function () {
  "use strict";
  const tau = Math.PI * 2;
  const clamp = x => Math.min(1, Math.max(0, x));

  function build(T, root, genes, colors) {
    const val = (key, fallback) => Number.isFinite(genes[key]) ? clamp(genes[key]) : fallback;
    const fat = val("fullness", .45), white = val("white", .55);
    const bands = val("bands", .45), phase = val("patternSeed", .4) * tau;
    const kitten = genes.age === "kitten";
    const { dark, light, ink, warm } = colors;
    const material = new T.MeshStandardMaterial({vertexColors:true, roughness:1});
    material.onBeforeCompile = shader=>{
      shader.uniforms.paperIvory={value:light};
      shader.vertexShader=shader.vertexShader
        .replace("#include <common>","#include <common>\nattribute float furBoundary; varying float vBoundary;")
        .replace("#include <begin_vertex>","#include <begin_vertex>\nvBoundary=furBoundary;");
      shader.fragmentShader=shader.fragmentShader
        .replace("#include <common>","#include <common>\nuniform vec3 paperIvory; varying float vBoundary;")
        .replace("#include <color_fragment>",`#include <color_fragment>
          float aa=max(.003,fwidth(vBoundary)*.78);
          diffuseColor.rgb=mix(diffuseColor.rgb,paperIvory,smoothstep(-aa,aa,vBoundary));`);
    };

    function surface(name, rings, pigment, divisions=4) {
      const xyz=[], rgb=[], normals=[], indices=[], boundary=[];
      const normal = new T.Vector3(), ab=new T.Vector3(), ac=new T.Vector3();
      const tone = new T.Color(), col = new T.Color();
      const frame = new T.Vector3();
      function triangle(a,b,c) {
        ab.subVectors(b,a); ac.subVectors(c,a); normal.crossVectors(ab,ac);
        if (normal.lengthSq()<1e-13) return;
        normal.normalize();
        // A restrained top-light is painted into the coat as its cool charcoal
        // planes turn up into the light, including in the small inventory icon.
        tone.copy(dark).lerp(warm, .16 + .40*clamp(normal.y*.66+normal.x*.18+.33));
        const previous=[];
        for (let i=0;i<=divisions;i++) {
          const row=[];
          for (let j=0;j<=divisions-i;j++) {
            frame.copy(a).addScaledVector(ab,i/divisions).addScaledVector(ac,j/divisions);
            const [stripe,milk]=pigment(frame);
            col.copy(tone).lerp(ink, clamp(stripe));
            const idx=xyz.length/3;
            xyz.push(frame.x,frame.y,frame.z); rgb.push(col.r,col.g,col.b);
            normals.push(normal.x,normal.y,normal.z); boundary.push(milk); row.push(idx);
            if(i>0) {
              indices.push(previous[j],idx,previous[j+1]);
              if(j>0) indices.push(row[j-1],idx,previous[j]);
            }
          }
          previous.splice(0,previous.length,...row);
        }
      }
      const sides = rings[0].length;
      for(let j=1;j<rings.length;j++) {
        for(let i=0;i<sides;i++) {
          const nx=(i+1)%sides;
          const a=rings[j-1][i], b=rings[j-1][nx], c=rings[j][i], d=rings[j][nx];
          if((i<sides/2) === !(j%2)) {triangle(a,b,c);triangle(b,d,c);}
          else {triangle(a,b,d);triangle(a,d,c);}
        }
      }
      for (const [idx,top] of [[0,false],[rings.length-1,true]]) {
        const ring=rings[idx], center=new T.Vector3();
        for(const p of ring) center.add(p);
        center.divideScalar(sides);
        for(let i=0;i<sides;i++) {
          const nx=(i+1)%sides;
          if(top) triangle(center,ring[i],ring[nx]); else triangle(center,ring[nx],ring[i]);
        }
      }
      const geo=new T.BufferGeometry();
      geo.setAttribute("position",new T.Float32BufferAttribute(xyz,3));
      geo.setAttribute("color",new T.Float32BufferAttribute(rgb,3));
      geo.setAttribute("normal",new T.Float32BufferAttribute(normals,3));
      geo.setAttribute("furBoundary",new T.Float32BufferAttribute(boundary,1));
      geo.setIndex(indices);
      const mesh=new T.Mesh(geo,material);
      mesh.name=name; root.add(mesh);
    }

    // Each section identifies its own front and rear silhouette rather than
    // scaling a tube around one fixed central axis.
    function upright(rows, sides, xAt=()=>0, slope=0) {
      return rows.map(([y,side,front,back])=>Array.from({length:sides},(_,i)=>{
        const a=tau*i/sides;
        const z=(front+back)*.5+(front-back)*.5*Math.cos(a);
        const x=xAt(y) + side*Math.sin(a);
        // Angle the planes towards the projecting knee or brisket, keeping
        // the sole and the joint where the limb enters the body stationary.
        const progress=(y-rows[0][0])/(rows[rows.length-1][0]-rows[0][0]);
        const rise=slope*Math.sin(Math.PI*progress)**2*Math.cos(a);
        return new T.Vector3(x,y+rise,z);
      }));
    }
    function lying(rows,sides) {
      // +Z is the neck. Winding matches the upward adult sections.
      return rows.map(([z,side,top,bottom])=>Array.from({length:sides},(_,i)=>{
        const a=tau*i/sides;
        return new T.Vector3(-side*Math.sin(a), (top+bottom)*.5+(top-bottom)*.5*Math.cos(a), z);
      }));
    }
    function band(distance, radius) {
      return clamp((radius-Math.abs(distance))/.055);
    }
    const rows=kitten?[
      [-1.03,.13,.42,.16],[-.91,.34+fat*.10,.59+fat*.09,.075],
      [-.64,.39+fat*.14,.68+fat*.12,.07],[-.30,.38+fat*.13,.65+fat*.12,.065],
      [.015,.33+fat*.10,.58+fat*.10,.07],[.35,.35+fat*.07,.62+fat*.07,.095],
      [.59,.29+fat*.07,.63+fat*.04,.15],[.79,.22,.61,.26]
    ]:[
      [.10,.27+fat*.06,-.16,-.65],[.22,.45+fat*.12,-.018+fat*.06,-.78-fat*.10],
      [.49,.52+fat*.21,.24+fat*.16,-.89-fat*.15],[.76,.49+fat*.21,.35+fat*.20,-.82-fat*.14],
      [1.01,.43+fat*.17,.45+fat*.17,-.63-fat*.14],[1.26,.38+fat*.13,.56+fat*.14,-.39-fat*.12],
      [1.47,.385+fat*.10,.63+fat*.10,-.22-fat*.09],[1.67,.353+fat*.08,.58+fat*.085,-.075-fat*.065],
      [1.82,.29+fat*.05,.52+fat*.06,.035-fat*.04],[1.93,.275,.46,.10]
    ];
    const torso=kitten?lying(rows,14):upright(rows,14,()=>0,.15);
    function trunkPaint(p) {
      const flank=Math.abs(p.x);
      const bodyLine=kitten ? p.z+.54 : p.y*.70 + .30*p.z;
      const frequency=1.8+bands*3.4;
      const wave=bodyLine*frequency + (kitten ? .98*p.y : .60*p.z) + phase*.23;
      const broken=clamp((flank-.08)*3.8)*clamp(kitten ? (p.y-.20)*4.1 : (.37-p.z)*3.4);
      const stripe=band(wave-Math.round(wave),.16)*broken*(.26+.42*bands);
      const center=.032*Math.sin(phase+bodyLine*2.2);
      const width=kitten?.20+white*.33:.25+white*.41;
      const ruffEdge=.15 + .32*Math.max(0,1.62-p.y) - .22*Math.max(0,p.y-1.62)
        + .025*Math.cos(12*p.y+phase);
      const badge=kitten ? Math.min((p.z-.32)*2.1,(.42-p.y)*2.8) :
        Math.min((p.y -.76+.36*white-1.55*Math.abs(p.x-center))*1.15, (p.z-ruffEdge)*2);
      const bib=Math.min(width-Math.abs(p.x-center),badge);
      return [stripe, bib];
    }
    surface("bodyCoat",torso,trunkPaint,7);

    for(const s of [-1,1]) {
      // Both wrists follow the upright weight line. A small forward step
      // on one side separates the two paws in the three-quarter camera.
      const dz=s===1?.095:-.040;
      const front=kitten?[
        [.011,.088,1.16+dz,.79+dz],[.05,.142,1.22+dz,.76+dz],
        [.107,.141,1.21+dz,.74+dz],[.164,.116,1.10+dz,.68+dz],
        [.215,.112,.94+dz,.56+dz],[.33,.118,.77+dz,.46+dz],[.43,.07,.66,.47]
      ]:[
        [.010,.096,.78+dz,.46+dz],[.049,.147,.829+dz,.418+dz],
        [.114,.153,.807+dz,.403+dz],[.167,.122,.711+dz,.412+dz],
        [.28,.088,.594+dz,.388+dz],[.56,.097,.546+dz,.319+dz],
        [.80,.116,.515+dz,.237+dz],[1.05,.133+fat*.016,.491+dz,.176+dz],
        [1.24,.130+fat*.031,.482+dz,.102+dz],[1.44,.095,.45,.15],
        [1.55,.041,.42,.26]
      ];
      const pawX=kitten?.255:.203;
      const adjustedFront=front.map(([y,r,z1,z2])=>{
        const retreat=kitten ? Math.max(0,y-.19)*.27 : Math.max(0,y-.63)*.40;
        return [y,r,z1-retreat,z2-retreat];
      });
      const foreRings=upright(adjustedFront,10,y=>{
        const shoulder=kitten?0:Math.max(0,y-.80)*.18;
        return s*(pawX+(kitten?0:.044*Math.sin(Math.min(1,y)*2))-shoulder);
      });
      surface("foreleg",foreRings,p=>{
        const boundary=(kitten?.16:.18)+(s===1?.12:.06)*white+.24*white;
        const cut=boundary+.13*(p.z-.51)+.032*Math.sin(phase+s);
        const sock=cut-p.y;
        const bib=trunkPaint(p)[1];
        return [0,Math.max(sock,bib)];
      });
      // The rounded front of the folded stifle and tucked hock have their
      // own profile. The knee comes back into the flank above the ankle.
      const hind=kitten?[
        [.010,.08,-.18,-.56],[.061,.14,-.11,-.61],[.105,.155,-.17,-.69],
        [.19,.16,-.23,-.73],[.31,.17+fat*.035,-.29,-.79],
        [.44,.137,-.39,-.81],[.53,.06,-.53,-.70]
      ]:[
        [.009,.105,.045,-.43],[.051,.175,.090,-.54],
        [.139,.185,.078,-.59],[.21,.172,-.055,-.64],
        [.35,.201+fat*.043,-.032,-.72],[.54,.232+fat*.073,.106,-.71-fat*.06],
        [.71,.217+fat*.08,.145,-.62-fat*.06],[.85,.177+fat*.075,.071,-.56],
        [1.02,.112+fat*.044,-.030,-.40],[1.12,.048,-.07,-.22]
      ];
      const hindX=kitten?.335:.402;
      surface("haunch",upright(hind,12,y=>{
        // The stifle projects at mid-height; the top dives medially into the pelvis.
        const knee=kitten?.032*Math.sin(5*y):.053*Math.sin(3.6*y);
        const tuck=Math.max(0,y-(kitten?.30:.72))*(kitten?.34:.31);
        return s*(hindX+knee-tuck);
      },kitten?-.052:-.145),p=>{
        const sock=.081+white*.060+.042*p.z-p.y;
        const mark=clamp((p.y-.20)*2.4)*band((p.y*3+.68*p.z+phase*.15)%1-.50,.17);
        return [.27*mark, sock];
      });
    }

    // The tail is a loose comma on the floor outside the hip. Its tip turns
    // upwards, leaving daylight between it and the body in the fixed camera.
    const tail=kitten?[
      [-.08,.29,-.88,.117],[-.14,.21,-1.05,.12],[-.18,.145,-1.24,.108],
      [-.23,.098,-1.40,.094],[-.29,.105,-1.55,.084],[-.35,.141,-1.66,.067],[-.37,.17,-1.71,.028]
    ]:[
      [.13,.40,-.69,.137],[.248,.272,-.84-fat*.09,.146],[.40,.16,-.98-fat*.10,.14],
      [.57,.124,-1.08-fat*.08,.121],[.76,.107,-1.12-fat*.04,.108],
      [.92,.115,-1.08,.096],[1.034,.18,-.99,.079],[1.093,.264,-.93,.056],[1.102,.301,-.91,.022]
    ];
    const tailRings=tail.map(([x,y,z,r],i)=>{
      const before=tail[Math.max(0,i-1)], after=tail[Math.min(tail.length-1,i+1)];
      const tangent=new T.Vector3(after[0]-before[0],after[1]-before[1],after[2]-before[2]).normalize();
      const right=new T.Vector3().crossVectors(new T.Vector3(0,1,0),tangent).normalize();
      const up=new T.Vector3().crossVectors(tangent,right).normalize();
      return Array.from({length:9},(_,j)=>{
        const angle=tau*j/9;
        return new T.Vector3(x,y,z).addScaledVector(right,r*Math.cos(angle)).addScaledVector(up,r*Math.sin(angle));
      });
    });
    surface("tail",tailRings,p=>{
      const t=kitten?clamp((-p.z-.92)/.79):clamp((p.x-.16)/.94);
      const cycle=t*(3.1+bands*1.9)+.11*Math.sin(phase);
      return [.62*band(cycle-Math.round(cycle),.245),t-(1.26-.27*white)];
    });
  }
  window.PaperBody={build};
})();
