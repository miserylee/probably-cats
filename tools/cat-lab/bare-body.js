/* Continuous bare skin over the ribcage and the four limb roots. The original
   carved sections define the muscles, paws, and their paint coordinates. */
(() => {
  "use strict";

  const mix = (a,b,t) => a+(b-a)*t;
  const clamp = t => Math.max(0,Math.min(1,t));
  const cache = new Map();

  function readPart(mesh,sides,baby) {
    const geometry=mesh.geometry, pos=geometry.getAttribute("position");
    // The seated cat has horizontal sections. The prone cat's sections progress
    // from its muzzle to its tail, so use -z as its longitudinal coordinate.
    const rings=[];
    for(let i=0;i<pos.count;i+=sides) {
      const points=[];
      let station=0;
      for(let j=i;j<i+sides;j++) {
        const axial=baby ? -pos.getZ(j) : pos.getY(j);
        points.push({x:pos.getX(j),b:baby ? pos.getY(j) : pos.getZ(j),s:axial,index:j});
        station+=axial;
      }
      rings.push({points,s:station/sides});
    }
    return {geometry,rings,sides,name:mesh.name};
  }

  function cut(part,s) {
    const rings=part.rings, last=rings.length-1;
    let hi=1;
    while(hi<last && rings[hi].s<s) hi++;
    const low=rings[hi-1],high=rings[hi];
    let xmin=Infinity,xmax=-Infinity,bmin=Infinity,bmax=-Infinity;
    const points=low.points.map((a,j)=>{
      const b=high.points[j];
      const t=clamp((s-a.s)/Math.max(.0001,b.s-a.s));
      const x=mix(a.x,b.x,t), v=mix(a.b,b.b,t);
      xmin=Math.min(xmin,x); xmax=Math.max(xmax,x);
      bmin=Math.min(bmin,v); bmax=Math.max(bmax,v);
      return {x,b:v,lo:a.index,hi:b.index,t};
    });
    const edges=points.map((a,j)=>{
      const b=points[(j+1)%points.length],dx=b.x-a.x,db=b.b-a.b;
      return {a,b,dx,db,inv:1/Math.max(1e-12,dx*dx+db*db)};
    });
    return {edges,xmin,xmax,bmin,bmax,cap:Math.max(rings[0].s-s,s-rings[last].s)};
  }

  // The closest edge also locates the surface in the original paint charts.
  // This avoids projecting an unrelated paw or the opposite leg onto new skin.
  function nearest(section,x,b,result) {
    let minimum=Infinity, inside=false, index=0, fraction=0;
    const edges=section.edges;
    for(let j=0;j<edges.length;j++) {
      const e=edges[j],px=x-e.a.x,pb=b-e.a.b;
      const t=clamp((px*e.dx+pb*e.db)*e.inv);
      const dx=px-t*e.dx, db=pb-t*e.db;
      const distance=dx*dx+db*db;
      if(distance<minimum) { minimum=distance;index=j;fraction=t; }
      if((e.a.b>b)!==(e.b.b>b) && px<e.dx*pb/e.db) inside=!inside;
    }
    if(result) {result.edge=edges[index];result.t=fraction;}
    const radial=(inside?-1:1)*Math.sqrt(minimum), cap=section.cap;
    return radial>0 && cap>0 ? Math.hypot(radial,cap) : Math.max(radial,cap);
  }

  function distance(section,x,b,band) {
    const gap=Math.max(section.xmin-x,x-section.xmax,section.bmin-b,b-section.bmax,section.cap);
    return gap>band ? gap : nearest(section,x,b);
  }

  const corners = [[0,0,0],[1,0,0],[0,1,0],[1,1,0],[0,0,1],[1,0,1],[0,1,1],[1,1,1]];
  const cubeEdges = [[0,1],[2,3],[4,5],[6,7],[0,2],[1,3],[4,6],[5,7],[0,4],[1,5],[2,6],[3,7]];

  function skinMesh(parts,baby) {
    // Resolve tiny toe ridges as well as the larger elbow and hip contours.
    const step=baby?.022:.03;
    const min=[Infinity,Infinity,Infinity],max=[-Infinity,-Infinity,-Infinity];
    for(const part of parts) for(const ring of part.rings) for(const p of ring.points) {
      for(const [axis,v] of [[0,p.x],[1,p.b],[2,p.s]]) {
        min[axis]=Math.min(min[axis],v);max[axis]=Math.max(max[axis],v);
      }
    }
    const origin=min.map(v=>(Math.floor(v/step)-4)*step);
    const [nx,nb,ns]=max.map((v,i)=>Math.ceil((v-origin[i])/step)+5);
    const plane=nx*nb,count=plane*ns;
    const field=new Float32Array(count),owner=new Uint8Array(count);
    const joiningLimb=new Uint8Array(count),limbShare=new Float32Array(count);
    for(let k=0;k<ns;k++) {
      const s=origin[2]+k*step,sections=parts.map(part=>cut(part,s));
      const bands=parts.map((part,i)=>{
        if(i===0)return 0;
        // A narrow fillet remains where the tucked chest rests on the wrist.
        // Its width grows toward the shoulder, leaving the toes free.
        if(baby) return part.name==="foreleg" ? .065+.070*clamp((s+.93)/.50) : .15;
        const proximal=clamp((s-.16)/.63);
        return .025+(part.name==="foreleg"?.22:.26)*proximal;
      });
      for(let j=0;j<nb;j++) for(let i=0;i<nx;i++) {
        const x=origin[0]+i*step,b=origin[1]+j*step,index=k*plane+j*nx+i;
        const trunk=distance(sections[0],x,b,baby?.27:.47);
        let skin=trunk,side=0,share=0;
        for(let p=1;p<sections.length;p++) {
          const band=bands[p], limb=distance(sections[p],x,b,band+.055);
          // Each root blends only into the ribcage: opposed paws, the curled
          // tail and other limbs that merely touch keep their natural overlap.
          const h=Math.max(0,1-Math.abs(trunk-limb)/Math.max(1e-6,band));
          const joined=Math.min(trunk,limb)-.25*band*h*h;
          if(joined<skin) {
            skin=joined; side=p;
            share=band>0 ? clamp(.5+.5*(trunk-limb)/band) : Number(limb<trunk);
          }
        }
        field[index]=skin; owner[index]=share>.5?side:0;
        joiningLimb[index]=side;limbShare[index]=share;
      }
    }

    // Surface nets: one vertex per cut cell, joined across every grid edge
    // crossed by the skin. All charts share the same position and field normal.
    const offsets=corners.map(([x,b,s])=>x+b*nx+s*plane);
    const cells=new Int32Array(count).fill(-1), points=[],normals=[],morphs=[];
    const sample=new Float64Array(8), weights=new Float64Array(parts.length);
    const sectionsByStation=parts.map(()=>new Map());
    function project(part,x,b,s) {
      // A much finer axial spacing than the extraction cells keeps the coat
      // charts smooth while sharing cross-sections between adjacent vertices.
      const q=Math.round((s-origin[2])*12/step);
      const slices=sectionsByStation[part];
      let section=slices.get(q);
      if(!section) {
        section=cut(parts[part],origin[2]+q*step/12);slices.set(q,section);
      }
      const location={};
      nearest(section,x,b,location);
      return location;
    }
    function interpolate(attr,location,c) {
      const {a,b}=location.edge, values=attr.array, size=attr.itemSize;
      const left=mix(values[a.lo*size+c],values[a.hi*size+c],a.t);
      const right=mix(values[b.lo*size+c],values[b.hi*size+c],b.t);
      return mix(left,right,location.t);
    }
    for(let k=0;k<ns-1;k++) for(let j=0;j<nb-1;j++) for(let i=0;i<nx-1;i++) {
      const n=k*plane+j*nx+i;
      const sign=field[n]<0;
      if(offsets.every(d=>(field[n+d]<0)===sign)) continue;
      for(let h=0;h<8;h++) sample[h]=field[n+offsets[h]];
      let x=0,b=0,s=0,edges=0;
      for(const [a,c] of cubeEdges) {
        if((sample[a]<0)===(sample[c]<0))continue;
        const t=sample[a]/(sample[a]-sample[c]);
        x+=mix(corners[a][0],corners[c][0],t);
        b+=mix(corners[a][1],corners[c][1],t);
        s+=mix(corners[a][2],corners[c][2],t); edges++;
      }
      x/=edges; b/=edges; s/=edges;
      weights.fill(0);
      for(let h=0;h<8;h++) {
        const [ix,ib,is]=corners[h], g=n+offsets[h];
        const w=(ix?x:1-x)*(ib?b:1-b)*(is?s:1-s);
        weights[0]+=w*(1-limbShare[g]);
        weights[joiningLimb[g]]+=w*limbShare[g];
      }
      x=origin[0]+step*(i+x);
      b=origin[1]+step*(j+b);
      s=origin[2]+step*(k+s);
      // The derivative of the smooth union gives the contribution of each
      // muscle. Transport the original smooth anatomical normals with these
      // weights rather than shading the facets of the sampled polygon rings.
      const normal=[0,0,0], displacement=[0,0,0], leanNormal=[0,0,0], fullNormal=[0,0,0];
      for(let p=0;p<parts.length;p++) {
        if(weights[p]<1e-5)continue;
        const location=project(p,x,b,s), part=parts[p];
        function accumulateNormal(geometry,target) {
          const attr=geometry.getAttribute("normal");
          const dx=interpolate(attr,location,0),dy=interpolate(attr,location,1),dz=interpolate(attr,location,2);
          const w=weights[p]/(Math.hypot(dx,dy,dz)||1);
          target[0]+=w*dx; target[1]+=w*dy; target[2]+=w*dz;
        }
        accumulateNormal(part.geometry,normal);
        accumulateNormal(part.lean,leanNormal);
        accumulateNormal(part.full,fullNormal);
        for(let c=0;c<3;c++) {
          const from=interpolate(part.lean.getAttribute("position"),location,c);
          const to=interpolate(part.full.getAttribute("position"),location,c);
          displacement[c]+=weights[p]*(to-from);
        }
      }
      cells[n]=points.length;
      points.push([x,b,s]);
      for(const n of [normal,leanNormal,fullNormal]) {
        const length=Math.hypot(...n)||1;
        for(let c=0;c<3;c++) n[c]/=length;
      }
      normals.push(normal);
      morphs.push([...displacement,...leanNormal,...fullNormal]);
    }
    const charts=parts.map(part=>{
      const attrs=Object.entries(part.geometry.attributes).filter(([name])=>name!=="normal" && name!=="position");
      return {attrs,channels:attrs.map(()=>[]),position:[],normal:[],morph:[],indices:[],ids:new Map()};
    });
    function vertex(p,id) {
      const chart=charts[p];
      if(chart.ids.has(id))return chart.ids.get(id);
      const local=chart.position.length/3;
      chart.ids.set(id,local);
      const [x,b,s]=points[id], location=project(p,x,b,s);
      chart.position.push(x,baby?b:s,baby?-s:b);
      chart.normal.push(...normals[id]);
      chart.morph.push(...morphs[id]);
      chart.attrs.forEach(([,attr],i)=>{
        for(let c=0;c<attr.itemSize;c++) {
          chart.channels[i].push(interpolate(attr,location,c));
        }
      });
      return local;
    }
    // For each sign-changing edge, the surrounding four cells form one quad.
    const axes=[[1,nx,plane],[nx,plane,1],[plane,1,nx]];
    for(let k=1;k<ns-1;k++) for(let j=1;j<nb-1;j++) for(let i=1;i<nx-1;i++) {
      const n=k*plane+j*nx+i;
      for(const [axis,u,v] of axes) {
        const end=n+axis;
        if((field[n]<0)===(field[end]<0))continue;
        const p=owner[Math.abs(field[n])<Math.abs(field[end])?n:end];
        const a=vertex(p,cells[n]),b=vertex(p,cells[n-u]),c=vertex(p,cells[n-u-v]),d=vertex(p,cells[n-v]);
        if(field[n]<0)charts[p].indices.push(a,c,b,a,d,c);
        else charts[p].indices.push(a,b,c,a,c,d);
      }
    }
    return charts.map(chart=>{
      const attributes={
        position:new Float32Array(chart.position),normal:new Float32Array(chart.normal)
      };
      chart.attrs.forEach(([name],i)=>{attributes[name]=new Float32Array(chart.channels[i]);});
      return {attributes,morph:new Float32Array(chart.morph),
        index:chart.position.length/3>65535?new Uint32Array(chart.indices):new Uint16Array(chart.indices)};
    });
  }

  // Solve the junctions once per pose. Body condition in WoodBody moves each
  // section linearly. Bind the shared skin to those same sections, including
  // both muscles' influence on every vertex in the blend.
  function makeTemplate(T,genes,material) {
    const baby=genes.age==="kitten";
    const counts={"Body and ruff":baby?28:24,foreleg:baby?26:18,haunch:baby?22:18};
    const poses=[0,.5,1].map(fullness=>{
      const group=new T.Group();
      window.WoodBody.build(T,group,{...genes,fullness},{material});
      return group;
    });
    const [lean,neutral,full]=poses;
    try {
      window.CatCoordinates.attach(T,neutral,genes.age,neutral,genes.age+"-skin");
      const parts=[];
      neutral.children.forEach((mesh,i)=>{
        if(!counts[mesh.name])return;
        const part=readPart(mesh,counts[mesh.name],baby);
        part.lean=lean.children[i].geometry;
        part.full=full.children[i].geometry;
        parts.push(part);
      });
      return skinMesh(parts,baby);
    } finally {
      for(const pose of poses) for(const mesh of pose.children)mesh.geometry.dispose();
    }
  }
  function shapeAt(part,fullness) {
    const {attributes,morph}=part;
    if(fullness===.5)return attributes;
    const position=new Float32Array(attributes.position.length), normal=new Float32Array(position.length);
    const offset=fullness-.5, amount=2*Math.abs(offset);
    for(let j=0,m=0;j<position.length;j+=3,m+=9) {
      const n=m+(offset<0?3:6);
      for(let c=0;c<3;c++){
        position[j+c]=attributes.position[j+c]+offset*morph[m+c];
        normal[j+c]=mix(attributes.normal[j+c],morph[n+c],amount);
      }
      const inv=1/Math.sqrt(normal[j]**2+normal[j+1]**2+normal[j+2]**2 || 1);
      normal[j]*=inv; normal[j+1]*=inv; normal[j+2]*=inv;
    }
    return {...attributes,position,normal};
  }
  function join(T,root,genes) {
    const meshes=root.children.filter(mesh=>mesh.name==="Body and ruff" || mesh.name==="foreleg" || mesh.name==="haunch");
    const age=genes.age==="kitten" ? "kitten" : "adult";
    const fullness=clamp(genes.fullness??.5);
    let pose=cache.get(age);
    if(!pose) {
      pose={parts:makeTemplate(T,{...genes,age},meshes[0].material)};
      cache.set(age,pose);
    }
    if(fullness!==pose.fullness) {
      pose.shaped=pose.parts.map(part=>shapeAt(part,fullness));
      pose.fullness=fullness;
    }
    meshes.forEach((mesh,i)=>{
      const old=mesh.geometry,geometry=new T.BufferGeometry();
      for(const [name,values] of Object.entries(pose.shaped[i])) {
        geometry.setAttribute(name,new T.BufferAttribute(values,old.getAttribute(name).itemSize));
      }
      geometry.setIndex(new T.BufferAttribute(pose.parts[i].index,1));
      geometry.computeBoundingSphere();
      mesh.geometry=geometry;old.dispose();
    });
    if(age==="adult")window.CatBareNeck.join(T,root,meshes[0],fullness);
  }
  window.CatBareBody={join};
})();
