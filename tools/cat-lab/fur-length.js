/* The short coat and exposed skin use the same carving. Only long fur adds
   volume, growing away from the body in the direction the coat hangs. */
(() => {
  "use strict";

  function smooth(a,b,x) {
    const t=Math.min(1,Math.max(0,(x-a)/(b-a)));
    return t*t*(3-2*t);
  }
  const bell=x=>Math.exp(-x*x);
  const mix=(a,b,t)=>a+(b-a)*t;

  function growCheeks(p,baby) {
    const width=baby?.51:.70;
    const chin=baby?.31:1.98;
    const crown=baby?.98:2.80;
    const h=(p.y-chin)/(crown-chin);
    const sx=Math.abs(p.x)/width;
    const outside=smooth(.35,.97,sx);
    const jaw=bell((h-.29)/.32)*outside;
    const temple=baby ? 1-smooth(.89,1.20,p.z) : 1-smooth(.25,.65,p.z);
    const feather=jaw*(.15+.85*temple);
    const locks=.86+.14*Math.sin(h*24+sx*2.7);
    p.x+=Math.sign(p.x)*width*.19*feather*locks;
    p.y-=width*feather*(.24+.10*locks);
    p.z-=width*.08*feather;
  }

  function finish(T,geometry) {
    geometry.getAttribute("position").needsUpdate=true;
    geometry.computeVertexNormals();
    geometry.computeBoundingBox();
    geometry.computeBoundingSphere();
  }

  function sculptHead(T,head,baby) {
    const mesh=head.children.find(node=>node.isMesh);
    const geometry=mesh.geometry;
    const pos=geometry.getAttribute("position");
    const p=new T.Vector3();
    for(let i=0;i<pos.count;i++) {
      p.fromBufferAttribute(pos,i);
      growCheeks(p,baby);
      pos.setXYZ(i,p.x,p.y,p.z);
    }
    finish(T,geometry);
  }

  // The anatomical trunk and limbs are built from ordered rings. Working
  // around each ring's own center preserves bent wrists and asymmetric feet.
  function sculptBody(T,mesh,sides,baby) {
    const g=mesh.geometry,pos=g.getAttribute("position");
    const isTrunk=mesh.name==="Body and ruff", isTail=mesh.name==="tail";
    const p=new T.Vector3(),center=new T.Vector3();
    const total=pos.count/sides;
    for(let ring=0;ring<total;ring++) {
      center.set(0,0,0);
      const start=ring*sides;
      for(let j=0;j<sides;j++)center.add(p.fromBufferAttribute(pos,start+j));
      center.multiplyScalar(1/sides);
      const t=ring/(total-1);
      for(let j=0;j<sides;j++) {
        const n=start+j;
        p.fromBufferAttribute(pos,n);
        const h=p.y,dx=p.x-center.x,dz=p.z-center.z,dy=p.y-center.y;
        if(isTail) {
          const plume=1+smooth(0,.32,t)*(.31+.80*Math.sin(Math.PI*t));
          p.x=center.x+dx*plume;
          p.z=center.z+dz*plume;
          const sweep=smooth(.37,.95,t);
          p.x-=sweep*(baby?.065:.33);
          p.z+=sweep*(baby?-.065:.22);
          const lift=(baby?.059:.083)*smooth(0,.48,t);
          p.y=Math.max(.006,center.y+lift+dy*plume);
        } else if(isTrunk && !baby) {
          const mantle=bell((h-1.87)/.52);
          const pelvis=bell((h-.73)/.49);
          const extent=.71*mantle+.05*pelvis;
          p.x=center.x+dx*(1+extent);
          p.z=center.z+dz*(1+extent*.83);
          p.y-=mantle*.04+.035*pelvis;
        } else if(isTrunk) {
          const bib=bell((p.z-.74)/.48);
          const saddle=bell((p.z+.30)/.66);
          const extent=.44*bib+.15*saddle;
          p.x=center.x+dx*(1+extent);
          if(dy>0) p.y+=dy*.19*(bib+saddle);
          p.z+=.036*bib;
        } else if(mesh.name==="haunch") {
          const feather=smooth(.04,baby?.24:.62,h);
          p.x+=dx*.14*feather;
          if(baby) p.y+=Math.max(dy,0)*.14*feather;
          else p.z+=dz*.14*feather;
        } else {
          const cuff=baby ? smooth(1.25,.55,p.z) : smooth(.30,1.24,h);
          p.x+=dx*cuff*.27;
          if(baby) p.y+=Math.max(dy,0)*cuff*.35;
          else p.z+=dz*cuff*.24;
        }
        pos.setXYZ(n,p.x,p.y,p.z);
      }
    }
    finish(T,g);
  }

  // A lock follows a strip of the underlying carving. Each painted point
  // inherits the UVs and anatomical attributes of the surface directly below
  // it, so a tabby stripe continues across the swept ridge.
  function addLocks(T,root,mesh,baby) {
    const tail=mesh.name==="tail";
    const source=mesh.geometry,sides=tail?16:baby?28:24;
    const attrs=Object.entries(source.attributes).filter(([name])=>name!=="normal"&&name!=="fiberShade");
    const values=Object.fromEntries(attrs.map(([name])=>[name,[]]));
    const shades=[],indices=[];
    const rows=source.getAttribute("position").count/sides;
    const axis=[-1,-.40,0,.55,1];
    const sections=[
      [0,.72,-.012],[.23,1,.018],[.52,.92,.047],[.78,.68,.051],[1,.035,.022]
    ];
    const centers=[];
    if(tail) {
      const pos=source.getAttribute("position"), p=new T.Vector3();
      for(let r=0;r<rows;r++){
        const center=new T.Vector3();
        for(let j=0;j<sides;j++)center.add(p.fromBufferAttribute(pos,r*sides+j));
        centers.push(center.multiplyScalar(1/sides));
      }
    }

    function sample(ring,around) {
      const r=Math.min(rows-1.001,Math.max(0,ring));
      const a=((around%sides)+sides)%sides;
      const r0=Math.floor(r),a0=Math.floor(a),a1=(a0+1)%sides;
      const corners=[r0*sides+a0,r0*sides+a1,(r0+1)*sides+a0,(r0+1)*sides+a1];
      const u=a-a0,v=r-r0,weights=[(1-u)*(1-v),u*(1-v),(1-u)*v,u*v];
      const data={};
      for(const [name,attr] of attrs) {
        data[name]=Array.from({length:attr.itemSize},(_,i)=>
          corners.reduce((sum,index,k)=>sum+attr.array[index*attr.itemSize+i]*weights[k],0));
      }
      return data;
    }
    function lock(angle,drift,span,top,bottom,length) {
      const start=shades.length;
      const toSteps=sides/(2*Math.PI);
      const center=new T.Vector3(),normal=new T.Vector3(),direction=new T.Vector3();
      for(const [along,width,raise] of sections) {
        for(const across of axis) {
          const theta=angle+drift*along+span*width*across;
          const row=mix(top,bottom,along);
          const data=sample(row,theta*toSteps);
          const p=data.position;
          const peak=Math.max(0,1-Math.abs(across+.07));
          const offset=raise*(.24+.76*peak)+(baby?-.004:0);
          if(tail) {
            const i=Math.floor(row);
            center.copy(centers[i]).lerp(centers[i+1],row-i);
            normal.fromArray(p).sub(center).normalize();
            direction.copy(centers[i+1]).sub(centers[i]).normalize();
            for(let j=0;j<3;j++)p[j]+=normal.getComponent(j)*offset+
              direction.getComponent(j)*length*along*along;
            p[1]=Math.max(.007,p[1]);
          } else if(baby) {
            p[0]+=Math.sin(theta)*offset;
            p[1]+=Math.cos(theta)*offset;
            p[2]-=length*along*along;
          } else {
            p[0]+=Math.sin(theta)*offset;
            p[2]+=Math.cos(theta)*offset;
            p[1]-=length*along*along;
          }
          for(const [name] of attrs)values[name].push(...data[name]);
          shades.push(Math.sin(Math.PI*along)*mix(-.065,.058,peak));
        }
      }
      for(let y=1;y<sections.length;y++)for(let x=1;x<axis.length;x++){
        const a=start+(y-1)*axis.length+x-1,b=a+1,c=a+axis.length,d=c+1;
        // The collar runs downward on the adult, rearward on the kitten.
        if(baby||tail) indices.push(a,b,c,b,d,c);
        else indices.push(a,c,b,b,c,d);
      }
    }
    if(tail) {
      for(const [angle,drift,from,to] of [
        [.14,.23,.24,.64],[1.03,-.18,.31,.72],[1.98,.12,.21,.66],
        [2.91,.25,.40,.82],[3.79,-.16,.29,.69],[4.72,.17,.36,.77],[5.52,-.19,.44,.86]
      ]) lock(angle,drift,.43,from*(rows-1),to*(rows-1),baby?.10:.13);
    } else if(baby) {
      for(const side of [-1,1]) {
        lock(side*1.27,side*.25,.39,3.1,6.2,.10);
        lock(side*.83,side*.22,.37,4.1,6.7,.065);
      }
    } else {
      for(const side of [-1,1]) {
        lock(side*.69,side*.35,.42,17.0,10.7,.16);
        lock(side*1.32,side*.36,.39,16.9,11.8,.17);
        lock(side*1.77,side*.33,.39,16.0,11.5,.17);
        lock(side*2.39,side*.34,.40,15.1,10.2,.15);
        lock(side*.38,side*.18,.29,13.9,8.8,.15);
      }
    }
    const geometry=new T.BufferGeometry();
    for(const [name,attribute] of attrs)
      geometry.setAttribute(name,new T.Float32BufferAttribute(values[name],attribute.itemSize));
    geometry.setAttribute("fiberShade",new T.Float32BufferAttribute(shades,1));
    geometry.setIndex(indices);
    finish(T,geometry);
    const locks=new T.Mesh(geometry,mesh.material);
    locks.name=mesh.name;
    root.add(locks);
  }

  function apply(T,root,genes) {
    if(genes.furLength!=="long")return;
    const baby=genes.age==="kitten";
    const counts={"Body and ruff":baby?28:24,foreleg:baby?26:18,haunch:baby?22:18,tail:16};
    const torso=root.getObjectByName("Body and ruff");
    for(const part of root.children) {
      if(part.isMesh && counts[part.name])sculptBody(T,part,counts[part.name],baby);
      else if(part.isGroup && part.children.some(child=>child.isMesh))sculptHead(T,part,baby);
    }
    if(torso){
      addLocks(T,root,torso,baby);
      addLocks(T,root,root.getObjectByName("tail"),baby);
    }
  }
  window.CatFur={apply};
})();
